using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;
using System.Net;
using System.Net.Http.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

/// <summary>
/// Correction of sealed PersonnelPosition windows (ADR-0004 §2.7, ADR-0008): the sealed row stays untouched, a new row
/// records the corrected window, and a reason is required.
/// </summary>
[TestFixture]
public class AssignmentCorrectionApiTests : ApiTestBase
{
    private static readonly string[] Permissions =
    [
        "Organization.Personnel.Create",
        "Organization.Personnel.Read",
        "Organization.Position.Create",
        "Organization.PersonnelPosition.Create",
        "Organization.PersonnelPosition.Correct"
    ];

    private Guid _companyId;
    private AuthSession _session = null!;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _, _, _, _) = await SeedTestDataAsync();
        _session = await CreateAuthorizedClientAsync(_companyId, Permissions);
    }

    [Test]
    public async Task Correct_SealedAssignment_CreatesNewRowKeepsOldAndWritesAudit()
    {
        var (personnelId, positionId, sealedId) = await SeedSealedAssignmentAsync();
        var correctedFrom = DateTime.UtcNow.AddDays(-20);
        var correctedTo = DateTime.UtcNow.AddDays(-5);

        var response = await CorrectAsync(personnelId, sealedId, correctedFrom, correctedTo, isPrimary: true, "Date was entered one day late.");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var correctionId = (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var rows = await WithDbAsync(db => db.PersonnelPositions.AsNoTracking()
            .Where(pp => pp.PersonnelId == personnelId)
            .OrderBy(pp => pp.EffectiveFrom)
            .ToListAsync());
        rows.Count.ShouldBe(2);

        var sealedRow = rows.Single(r => r.Id == sealedId);
        sealedRow.EffectiveFrom.ShouldBe(DateTime.UtcNow.AddDays(-30), TimeSpan.FromSeconds(5));
        sealedRow.EffectiveTo.ShouldNotBeNull();
        sealedRow.CorrectsAssignmentId.ShouldBeNull();

        var correction = rows.Single(r => r.Id == correctionId);
        correction.PositionId.ShouldBe(positionId);
        correction.CorrectsAssignmentId.ShouldBe(sealedId);
        correction.EffectiveFrom.ShouldBe(correctedFrom, TimeSpan.FromSeconds(1));
        correction.EffectiveTo!.Value.ShouldBe(correctedTo, TimeSpan.FromSeconds(1));
        correction.IsPrimary.ShouldBeTrue();

        var audit = await WithDbAsync(db => db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.EventType == AuditEventTypes.PersonnelPositionCorrected));
        audit.EntityId.ShouldBe(correctionId);
        audit.BeforeData.ShouldNotBeNull();
        audit.BeforeData.ShouldContain(sealedId.ToString());
        audit.AfterData.ShouldNotBeNull();
        audit.AfterData.ShouldContain(correctionId.ToString());
        audit.Metadata!.ShouldContain("Date was entered one day late.");
    }

    [Test]
    public async Task Correct_OpenAssignment_Conflict()
    {
        var personnelId = await SeedPersonnelAsync("5000000001");
        var positionId = await SeedPositionAsync("CORR1");
        var openId = await SeedAssignmentAsync(personnelId, positionId, DateTime.UtcNow.AddDays(-5), effectiveTo: null);

        var response = await CorrectAsync(personnelId, openId, DateTime.UtcNow.AddDays(-4), DateTime.UtcNow.AddDays(-1), true, "oops");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("ASSIGNMENT_NOT_SEALED");
        (await WithDbAsync(db => db.PersonnelPositions.CountAsync(pp => pp.PersonnelId == personnelId))).ShouldBe(1);
    }

    [Test]
    public async Task Correct_WithoutReason_BadRequest()
    {
        var (personnelId, _, sealedId) = await SeedSealedAssignmentAsync("5000000002", "CORR2");

        var response = await CorrectAsync(personnelId, sealedId, DateTime.UtcNow.AddDays(-20), DateTime.UtcNow.AddDays(-5), true, "");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await WithDbAsync(db => db.PersonnelPositions.CountAsync(pp => pp.PersonnelId == personnelId))).ShouldBe(1);
    }

    [Test]
    public async Task Correct_AlreadyCorrected_Conflict()
    {
        var (personnelId, _, sealedId) = await SeedSealedAssignmentAsync("5000000003", "CORR3");
        (await CorrectAsync(personnelId, sealedId, DateTime.UtcNow.AddDays(-20), DateTime.UtcNow.AddDays(-5), true, "first"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await CorrectAsync(personnelId, sealedId, DateTime.UtcNow.AddDays(-19), DateTime.UtcNow.AddDays(-4), true, "again");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("ASSIGNMENT_ALREADY_CORRECTED");
        (await WithDbAsync(db => db.PersonnelPositions.CountAsync(pp => pp.PersonnelId == personnelId))).ShouldBe(2);
    }

    [Test]
    public async Task Correct_OverlappingAnotherActiveWindow_Conflict()
    {
        var personnelId = await SeedPersonnelAsync("5000000004");
        var positionId = await SeedPositionAsync("CORR4");
        var sealedId = await SeedAssignmentAsync(personnelId, positionId, DateTime.UtcNow.AddDays(-40), DateTime.UtcNow.AddDays(-20));
        await SeedAssignmentAsync(personnelId, positionId, DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(-1));

        var response = await CorrectAsync(personnelId, sealedId, DateTime.UtcNow.AddDays(-15), DateTime.UtcNow.AddDays(-5), true, "would overlap");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("ASSIGNMENT_OVERLAP");
    }

    private Task<HttpResponseMessage> CorrectAsync(Guid personnelId, Guid assignmentId, DateTime from, DateTime? to, bool isPrimary, string reason)
        => _session.Client.PostAsJsonAsync($"/api/v1/organization/personnel/{personnelId}/positions/{assignmentId}/corrections",
            new { EffectiveFrom = from, EffectiveTo = to, IsPrimary = isPrimary, Reason = reason });

    private async Task<(Guid PersonnelId, Guid PositionId, Guid SealedId)> SeedSealedAssignmentAsync(string nationalCode = "5000000000", string positionCode = "CORR0")
    {
        var personnelId = await SeedPersonnelAsync(nationalCode);
        var positionId = await SeedPositionAsync(positionCode);
        var sealedId = await SeedAssignmentAsync(personnelId, positionId, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(-10));
        return (personnelId, positionId, sealedId);
    }

    private Task<Guid> SeedPersonnelAsync(string nationalCode)
        => WithDbAsync(async db =>
        {
            var personnel = new Personnel(_companyId, nationalCode, "Corr", "Person", Gender.Female);
            db.Personnel.Add(personnel);
            await db.SaveChangesAsync(default);
            return personnel.Id;
        });

    private Task<Guid> SeedPositionAsync(string code)
        => WithDbAsync(async db =>
        {
            var position = new Position(_companyId, code, code);
            db.Positions.Add(position);
            await db.SaveChangesAsync(default);
            return position.Id;
        });

    private Task<Guid> SeedAssignmentAsync(Guid personnelId, Guid positionId, DateTime from, DateTime? effectiveTo)
        => WithDbAsync(async db =>
        {
            var personnel = await db.Personnel.Include(p => p.Positions).SingleAsync(p => p.Id == personnelId);
            var companies = new Dictionary<Guid, Guid> { [positionId] = _companyId };
            var assignment = personnel.AssignPosition(positionId, isPrimary: true, from, effectiveTo, DateTime.UtcNow, companies);
            await db.SaveChangesAsync(default);
            return assignment.Id;
        });

    private sealed record IdResponse(Guid Id);
}
