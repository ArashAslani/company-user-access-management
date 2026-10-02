using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

/// <summary>
/// Persistent audit (design §§42–45, ADR-0008): write paths emit the expected EventType, multi-mutation commands
/// share one OperationId, signature audit carries metadata only, and the read endpoint filters by actor / company /
/// date range / entity type.
/// </summary>
[TestFixture]
public class AuditApiTests : ApiTestBase
{
    private static readonly string[] WritePermissions =
    [
        "Organization.Personnel.Create",
        "Organization.Personnel.Edit",
        "Organization.Personnel.Read",
        "Organization.Position.Create",
        "Organization.Position.Edit",
        "Organization.Position.Read",
        "Organization.PersonnelPosition.Create",
        "Organization.PersonnelSignature.Create",
        "AccessManagement.Role.Create",
        "AccessManagement.Role.Edit",
        "AccessManagement.Role.Read",
        "AccessManagement.Role.Permissions.Manage",
        "AccessManagement.Role.BulkAssign",
        "AccessManagement.Role.Unassign",
        "AccessManagement.AuditLog.Read"
    ];

    private Guid _companyId;
    private Guid _appId;
    private AuthSession _session = null!;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _appId, _, _, _) = await SeedTestDataAsync();
        _session = await CreateAuthorizedClientAsync(_companyId, WritePermissions);
    }

    [Test]
    public async Task CreatePosition_WritesPositionCreated()
    {
        var response = await _session.Client.PostAsJsonAsync("/api/v1/organization/positions",
            new { Code = "AUD-POS", Title = "Audit Position", Status = PositionStatus.Active });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var audit = await SingleAsync(AuditEventTypes.PositionCreated);
        audit.EntityId.ShouldBe(id);
        audit.ActorUserId.ShouldBe(_session.UserId);
        audit.CompanyId.ShouldBe(_companyId);
        audit.OccurredAt.ShouldBe(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task UpdatePosition_ParentAndStatusChange_ShareOperationId()
    {
        var rootId = await SeedPositionAsync("AUD-ROOT");
        var childId = await SeedPositionAsync("AUD-CHILD");

        var response = await _session.Client.PutAsJsonAsync($"/api/v1/organization/positions/{childId}",
            new { Code = "AUD-CHILD", Title = "Child", ParentPositionId = rootId, Status = PositionStatus.Inactive });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var events = await WithDbAsync(db => db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityId == childId)
            .Select(a => new { a.EventType, a.OperationId })
            .ToListAsync());
        events.Select(e => e.EventType).OrderBy(x => x).ShouldBe([
            AuditEventTypes.PositionDeactivated,
            AuditEventTypes.PositionParentChanged,
            AuditEventTypes.PositionUpdated
        ]);
        events.Select(e => e.OperationId).Distinct().Count().ShouldBe(1);
    }

    [Test]
    public async Task AssignPrimaryPosition_WritesAssignedAndPrimaryChangedUnderSameOperation()
    {
        var personnelId = await SeedPersonnelAsync("6000000001");
        var positionId = await SeedPositionAsync("AUD-ASN");

        (await _session.Client.PostAsJsonAsync($"/api/v1/organization/personnel/{personnelId}/positions",
            new { PositionId = positionId, IsPrimary = true, EffectiveFrom = DateTime.UtcNow.AddDays(-1) }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var events = await WithDbAsync(db => db.AuditLogs.AsNoTracking()
            .Where(a => a.EventType == AuditEventTypes.PersonnelPositionAssigned
                || a.EventType == AuditEventTypes.PrimaryPositionChanged)
            .Select(a => new { a.EventType, a.OperationId })
            .ToListAsync());
        events.Count.ShouldBe(2);
        events.Select(e => e.OperationId).Distinct().Count().ShouldBe(1);
    }

    [Test]
    public async Task UploadSignature_MetadataOnly_NeverStoresBinary()
    {
        var personnelId = await SeedPersonnelAsync("6000000002");
        (await _session.Client.PostAsync($"/api/v1/organization/personnel/{personnelId}/signature",
            TestImages.SignatureUpload(TestImages.Png1x1))).StatusCode.ShouldBe(HttpStatusCode.Created);

        var audit = await SingleAsync(AuditEventTypes.SignatureUploaded);
        audit.Metadata.ShouldNotBeNull();
        audit.Metadata.ShouldContain("NewSignatureId");
        audit.Metadata.ShouldContain("NewHash");
        audit.Metadata.ShouldNotContain("iVBORw0KGgo");
        audit.BeforeData.ShouldBeNull();
        audit.AfterData.ShouldBeNull();
    }

    [Test]
    public async Task RoleAssignAndUnassign_WriteRoleAssignedAndRemoved()
    {
        var managing = await CreateManagingRoleAsync(_session, _companyId, "AUD-MGR", ScopeMode.None);
        var roleId = await CreateRoleAsync(_companyId, _appId, "Auditee", "AUD-ROLE", parentRoleId: managing.RoleId);
        var member = await CreateUserAsync("auditee@test.com", "Test123!");
        var membership = await CreateUserCompanyAsync(member.Id, _companyId, Guid.Empty);

        (await _session.Client.PostAsJsonAsync("/api/v1/access-control/roles/bulk-assign",
            new { RoleId = roleId, UserCompanyIds = new[] { membership.Id } })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SingleAsync(AuditEventTypes.RoleAssigned)).EntityType.ShouldBe(nameof(UserRole));

        (await _session.Client.DeleteAsync($"/api/v1/access-control/roles/{roleId}/users/{membership.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SingleAsync(AuditEventTypes.RoleRemoved)).EntityType.ShouldBe(nameof(UserRole));
    }

    [Test]
    public async Task GetAudit_FiltersByActorCompanyEntityTypeAndDateRange()
    {
        (await _session.Client.PostAsJsonAsync("/api/v1/organization/positions",
            new { Code = "AUD-F1", Title = "F1", Status = PositionStatus.Active })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await _session.Client.PostAsJsonAsync("/api/v1/organization/personnel",
            new { NationalCode = "6000000003", FirstName = "A", LastName = "B", Gender = 1 })).StatusCode.ShouldBe(HttpStatusCode.Created);

        var otherCompany = await CreateCompanyAsync("AUD-OTHER", "Other");
        var other = await CreateAuthorizedClientAsync("other-audit@test.com", otherCompany,
            "Organization.Position.Create", "AccessManagement.AuditLog.Read");
        (await other.Client.PostAsJsonAsync("/api/v1/organization/positions",
            new { Code = "AUD-F2", Title = "F2", Status = PositionStatus.Active })).StatusCode.ShouldBe(HttpStatusCode.Created);

        var byActor = await QueryAsync($"?actorUserId={_session.UserId}");
        byActor.Items.ShouldAllBe(i => i.ActorUserId == _session.UserId);
        byActor.Items.ShouldContain(i => i.EventType == AuditEventTypes.PositionCreated);
        byActor.Items.ShouldContain(i => i.EventType == AuditEventTypes.PersonnelCreated);

        var byEntity = await QueryAsync("?entityType=Position");
        byEntity.Items.ShouldNotBeEmpty();
        byEntity.Items.ShouldAllBe(i => i.EntityType == "Position");

        var byType = await QueryAsync($"?eventType={AuditEventTypes.PersonnelCreated}");
        byType.Items.Count.ShouldBe(1);
        byType.Items[0].EventType.ShouldBe(AuditEventTypes.PersonnelCreated);

        var past = await QueryAsync($"?to={Uri.EscapeDataString(DateTime.UtcNow.AddDays(-1).ToString("o"))}");
        past.Items.ShouldBeEmpty();

        var foreign = await other.Client.GetAsync("/api/v1/audit/access-history");
        foreign.StatusCode.ShouldBe(HttpStatusCode.OK);
        var foreignBody = await foreign.Content.ReadFromJsonAsync<PaginatedAudit>(JsonOptions);
        foreignBody!.Items.ShouldAllBe(i => i.CompanyId == otherCompany);
        foreignBody.Items.ShouldNotContain(i => i.ActorUserId == _session.UserId);
    }

    [Test]
    public async Task GetAudit_WithoutPermission_Forbidden()
    {
        var noAudit = await CreateAuthorizedClientAsync("no-audit@test.com", _companyId, "Organization.Position.Read");
        (await noAudit.Client.GetAsync("/api/v1/audit/access-history")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private Task<AuditLog> SingleAsync(string eventType)
        => WithDbAsync(db => db.AuditLogs.AsNoTracking().SingleAsync(a => a.EventType == eventType));

    private async Task<PaginatedAudit> QueryAsync(string query)
    {
        var response = await _session.Client.GetAsync($"/api/v1/audit/access-history{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PaginatedAudit>(JsonOptions))!;
    }

    private Task<Guid> SeedPersonnelAsync(string nationalCode)
        => WithDbAsync(async db =>
        {
            var personnel = new Personnel(_companyId, nationalCode, "Aud", "Person", Gender.Male);
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

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record IdResponse(Guid Id);
    private sealed record PaginatedAudit(List<AuditItem> Items, int TotalCount, int Page, int PageSize);
    private sealed record AuditItem(Guid Id, Guid? CompanyId, Guid ActorUserId, Guid OperationId, string EntityType, Guid EntityId, string EventType, DateTime OccurredAt);
}
