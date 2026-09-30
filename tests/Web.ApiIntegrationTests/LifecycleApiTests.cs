using CompanyAccessManagement.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;
using System.Net;
using System.Net.Http.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

/// <summary>
/// Personnel and position lifecycle rules enforced through the API: employment is never set directly, inactive positions
/// and personnel cannot be assigned, positions with current or upcoming assignments cannot be deactivated, and deleting
/// personnel is a soft delete.
/// </summary>
[TestFixture]
public class LifecycleApiTests : ApiTestBase
{
    private static readonly string[] OrganizationPermissions =
    [
        "Organization.Personnel.Read",
        "Organization.Personnel.Create",
        "Organization.Personnel.Edit",
        "Organization.Personnel.Delete",
        "Organization.Position.Read",
        "Organization.Position.Edit",
        "Organization.PersonnelPosition.Create",
        "Organization.PersonnelSignature.Create"
    ];

    private Guid _companyId;
    private AuthSession _session = null!;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _, _, _, _) = await SeedTestDataAsync();
        _session = await CreateAuthorizedClientAsync(_companyId, OrganizationPermissions);
    }

    [Test]
    public async Task CreatePersonnel_AsEmployed_ConflictAndNothingStored()
    {
        var response = await _session.Client.PostAsJsonAsync("/api/v1/organization/personnel",
            new { NationalCode = "4000000001", FirstName = "A", LastName = "B", Gender = 1, Status = PersonnelStatus.Employed });

        await ShouldBeConflictAsync(response, "PERSONNEL_STATUS_TRANSITION_INVALID");
        (await WithDbAsync(db => db.Personnel.AnyAsync(p => p.NationalCode == "4000000001"))).ShouldBeFalse();
    }

    [Test]
    public async Task UpdatePersonnel_DraftToEmployed_Conflict()
    {
        var personnelId = await SeedPersonnelAsync("4000000002");

        var response = await UpdatePersonnelAsync(personnelId, "4000000002", PersonnelStatus.Employed);

        await ShouldBeConflictAsync(response, "PERSONNEL_STATUS_TRANSITION_INVALID");
        (await StatusOfAsync(personnelId)).ShouldBe(PersonnelStatus.Draft);
    }

    [Test]
    public async Task UpdatePersonnel_DeactivateWithEffectivePosition_Conflict()
    {
        var personnelId = await SeedPersonnelAsync("4000000003");
        var positionId = await SeedPositionAsync("POS3");
        (await AssignViaApiAsync(personnelId, positionId)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await UpdatePersonnelAsync(personnelId, "4000000003", PersonnelStatus.Inactive);

        await ShouldBeConflictAsync(response, "PERSONNEL_STATUS_TRANSITION_INVALID");
        (await StatusOfAsync(personnelId)).ShouldBe(PersonnelStatus.Employed);
    }

    [Test]
    public async Task UpdatePersonnel_WithoutStatus_KeepsEmployment()
    {
        var personnelId = await SeedPersonnelAsync("4000000004");
        var positionId = await SeedPositionAsync("POS4");
        (await AssignViaApiAsync(personnelId, positionId)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await UpdatePersonnelAsync(personnelId, "4000000004", status: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StatusOfAsync(personnelId)).ShouldBe(PersonnelStatus.Employed);
    }

    [Test]
    public async Task AssignPosition_InactivePosition_Conflict()
    {
        var personnelId = await SeedPersonnelAsync("4000000005");
        var positionId = await SeedPositionAsync("POS5", PositionStatus.Inactive);

        var response = await AssignViaApiAsync(personnelId, positionId);

        await ShouldBeConflictAsync(response, "POSITION_INACTIVE");
        (await WithDbAsync(db => db.PersonnelPositions.AnyAsync(pp => pp.PersonnelId == personnelId))).ShouldBeFalse();
    }

    [Test]
    public async Task UpdatePosition_DeactivateWithCurrentAssignment_Conflict()
    {
        var personnelId = await SeedPersonnelAsync("4000000006");
        var positionId = await SeedPositionAsync("POS6");
        (await AssignViaApiAsync(personnelId, positionId)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await _session.Client.PutAsJsonAsync($"/api/v1/organization/positions/{positionId}",
            new { Code = "POS6", Title = "POS6", Status = PositionStatus.Inactive });

        await ShouldBeConflictAsync(response, "POSITION_HAS_ACTIVE_ASSIGNMENTS");
        (await WithDbAsync(db => db.Positions.Where(p => p.Id == positionId).Select(p => p.Status).SingleAsync())).ShouldBe(PositionStatus.Active);
    }

    [Test]
    public async Task DeletePersonnel_IsSoftDelete_KeepsSignatureAndBlocksAssignment()
    {
        var personnelId = await SeedPersonnelAsync("4000000007");
        var positionId = await SeedPositionAsync("POS7");
        var file = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 });
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        (await _session.Client.PostAsync($"/api/v1/organization/personnel/{personnelId}/signature",
            new MultipartFormDataContent { { file, "file", "signature.png" } })).StatusCode.ShouldBe(HttpStatusCode.Created);

        var delete = await _session.Client.DeleteAsync($"/api/v1/organization/personnel/{personnelId}");
        var assign = await AssignViaApiAsync(personnelId, positionId);

        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StatusOfAsync(personnelId)).ShouldBe(PersonnelStatus.Inactive);
        (await WithDbAsync(db => db.PersonnelSignatures.CountAsync(s => s.PersonnelId == personnelId))).ShouldBe(1);
        await ShouldBeConflictAsync(assign, "PERSONNEL_INACTIVE");
    }

    [Test]
    public async Task DeletePersonnel_WithEffectivePosition_Conflict()
    {
        var personnelId = await SeedPersonnelAsync("4000000008");
        var positionId = await SeedPositionAsync("POS8");
        (await AssignViaApiAsync(personnelId, positionId)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await _session.Client.DeleteAsync($"/api/v1/organization/personnel/{personnelId}");

        await ShouldBeConflictAsync(response, "EMPLOYED_HAS_ACTIVE_POSITION");
        (await StatusOfAsync(personnelId)).ShouldBe(PersonnelStatus.Employed);
    }

    private Task<Guid> SeedPersonnelAsync(string nationalCode)
        => WithDbAsync(async db =>
        {
            var personnel = new Personnel(_companyId, nationalCode, "Test", "Person", Gender.Female);
            db.Personnel.Add(personnel);
            await db.SaveChangesAsync(default);
            return personnel.Id;
        });

    private Task<Guid> SeedPositionAsync(string code, PositionStatus status = PositionStatus.Active)
        => WithDbAsync(async db =>
        {
            var position = new Position(_companyId, code, code);
            position.SetStatus(status, DateTime.UtcNow);
            db.Positions.Add(position);
            await db.SaveChangesAsync(default);
            return position.Id;
        });

    private Task<HttpResponseMessage> AssignViaApiAsync(Guid personnelId, Guid positionId)
        => _session.Client.PostAsJsonAsync($"/api/v1/organization/personnel/{personnelId}/positions",
            new { PositionId = positionId, IsPrimary = true, EffectiveFrom = DateTime.UtcNow.AddDays(-1), EffectiveTo = (DateTime?)null });

    private Task<HttpResponseMessage> UpdatePersonnelAsync(Guid personnelId, string nationalCode, PersonnelStatus? status)
        => _session.Client.PutAsJsonAsync($"/api/v1/organization/personnel/{personnelId}",
            new { FirstName = "Test", LastName = "Person", NationalCode = nationalCode, Gender = 1, Status = status });

    private Task<PersonnelStatus> StatusOfAsync(Guid personnelId)
        => WithDbAsync(db => db.Personnel.AsNoTracking().Where(p => p.Id == personnelId).Select(p => p.Status).SingleAsync());

    private static async Task ShouldBeConflictAsync(HttpResponseMessage response, string code)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain(code);
    }
}
