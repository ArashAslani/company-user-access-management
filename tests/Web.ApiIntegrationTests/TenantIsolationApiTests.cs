using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;
using System.Net;
using System.Net.Http.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

/// <summary>
/// A workspace (X-Company-Id) only sees and changes its own company's data:
/// naming another company explicitly is 403, reaching another company's entity by id is 404.
/// </summary>
[TestFixture]
public class TenantIsolationApiTests : ApiTestBase
{
    private Guid _companyId;
    private Guid _appId;
    private Guid _otherCompanyId;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _appId, _, _, _) = await SeedTestDataAsync();
        _otherCompanyId = await CreateCompanyAsync("OTHER", "Other Company");
    }

    [Test]
    public async Task GetPositions_OtherCompanyId_Forbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Read");

        var response = await session.Client.GetAsync($"/api/v1/organization/positions/?companyId={_otherCompanyId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task GetPositions_ReturnsOnlyWorkspaceCompany()
    {
        await SeedPositionAsync(_otherCompanyId, "FOREIGN");
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Read", "Organization.Position.Create");
        await CreatePositionAsync(session, _companyId, "LOCAL");

        var response = await session.Client.GetAsync("/api/v1/organization/positions/");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("LOCAL");
        body.ShouldNotContain("FOREIGN");
    }

    [Test]
    public async Task CreatePosition_OtherCompanyId_Forbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Create");

        var response = await session.Client.PostAsJsonAsync("/api/v1/organization/positions",
            new { CompanyId = _otherCompanyId, Code = "X", Title = "X" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await WithDbAsync(db => db.Positions.AnyAsync(p => p.CompanyId == _otherCompanyId))).ShouldBeFalse();
    }

    [Test]
    public async Task GetPosition_OtherCompanyEntity_NotFound()
    {
        var foreign = await SeedPositionAsync(_otherCompanyId, "FOREIGN");
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Read");

        (await session.Client.GetAsync($"/api/v1/organization/positions/{foreign}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await session.Client.GetAsync($"/api/v1/organization/positions/{foreign}/summary")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UpdatePosition_OtherCompanyEntity_NotFound()
    {
        var foreign = await SeedPositionAsync(_otherCompanyId, "FOREIGN");
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Read", "Organization.Position.Edit");

        var response = await session.Client.PutAsJsonAsync($"/api/v1/organization/positions/{foreign}",
            new { Code = "HACKED", Title = "Hacked", Status = PositionStatus.Active });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await WithDbAsync(db => db.Positions.AsNoTracking().Where(p => p.Id == foreign).Select(p => p.Code).SingleAsync())).ShouldBe("FOREIGN");
    }

    [Test]
    public async Task DeletePosition_OtherCompanyEntity_NotFound()
    {
        var foreign = await SeedPositionAsync(_otherCompanyId, "FOREIGN");
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Read", "Organization.Position.Delete");

        var response = await session.Client.DeleteAsync($"/api/v1/organization/positions/{foreign}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await WithDbAsync(db => db.Positions.AnyAsync(p => p.Id == foreign))).ShouldBeTrue();
    }

    [Test]
    public async Task PositionTree_UnrelatedHolding_Forbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Read");

        var response = await session.Client.GetAsync($"/api/v1/organization/positions/tree?holdingId={_otherCompanyId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task GetRoles_OtherCompanyId_Forbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read");

        var response = await session.Client.GetAsync($"/api/v1/access-control/roles/?companyId={_otherCompanyId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task CreateRole_OtherCompanyId_Forbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create");

        var response = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles",
            new { CompanyId = _otherCompanyId, ApplicationId = _appId, Code = "X", Name = "X", Kind = RoleKind.Standard });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await WithDbAsync(db => db.BusinessRoles.AnyAsync(r => r.CompanyId == _otherCompanyId))).ShouldBeFalse();
    }

    [Test]
    public async Task GetRole_OtherCompanyEntity_NotFound()
    {
        var foreign = await CreateRoleAsync(_otherCompanyId, _appId, "Foreign", "FOREIGN");
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read");

        var response = await session.Client.GetAsync($"/api/v1/access-control/roles/{foreign}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UpdateRole_OtherCompanyEntity_NotFound()
    {
        var foreign = await CreateRoleAsync(_otherCompanyId, _appId, "Foreign", "FOREIGN");
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Edit");

        var response = await session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{foreign}",
            new { Name = "Hacked", Code = "HACKED", Kind = RoleKind.Standard, Status = RoleStatus.Active });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await WithDbAsync(db => db.BusinessRoles.AsNoTracking().Where(r => r.Id == foreign).Select(r => r.Code).SingleAsync())).ShouldBe("FOREIGN");
    }

    [Test]
    public async Task DeleteRole_OtherCompanyEntity_NotFound()
    {
        var foreign = await CreateRoleAsync(_otherCompanyId, _appId, "Foreign", "FOREIGN");
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Delete");

        var response = await session.Client.DeleteAsync($"/api/v1/access-control/roles/{foreign}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await WithDbAsync(db => db.BusinessRoles.AsNoTracking().Where(r => r.Id == foreign).Select(r => r.Status).SingleAsync())).ShouldBe(RoleStatus.Active);
    }

    [Test]
    public async Task UpdateRolePermissions_OtherCompanyEntity_NotFound()
    {
        var foreign = await CreateRoleAsync(_otherCompanyId, _appId, "Foreign", "FOREIGN");
        var resourceId = await WithDbAsync(db => db.Resources.Where(r => r.ApplicationId == _appId && r.Code == "Products").Select(r => r.Id).SingleAsync());
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Permissions.Manage");

        var response = await session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{foreign}/permissions", new
        {
            Entries = new[] { new { ResourceId = resourceId, Actions = new[] { new { ActionCode = "Read", Effect = AccessEffect.Allow } } } }
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await WithDbAsync(db => (from ap in db.AuthPrincipals
                                  join ar in db.AccessRules on ap.Id equals ar.PrincipalId
                                  where ap.Type == PrincipalType.Role && ap.ReferenceId == foreign
                                  select ar.Id).AnyAsync())).ShouldBeFalse();
    }

    [Test]
    public async Task CopyRolePermissions_SourceInOtherCompany_NotFound()
    {
        var foreign = await CreateRoleAsync(_otherCompanyId, _appId, "Foreign", "FOREIGN");
        var local = await CreateRoleAsync(_companyId, _appId, "Local", "LOCAL");
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Permissions.Manage");

        var response = await session.Client.PostAsJsonAsync($"/api/v1/access-control/roles/{local}/permissions/copy-from",
            new { SourceRoleId = foreign, Mode = "APPEND" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task BulkAssign_UserCompanyInOtherCompany_NotFound()
    {
        var foreignUser = await CreateUserAsync("foreign@test.com", "Test123!");
        var foreignMembership = await CreateUserCompanyAsync(foreignUser.Id, _otherCompanyId, Guid.Empty);
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.BulkAssign");
        var manager = await CreateManagingRoleAsync(session, _companyId, "MANAGER", ScopeMode.None);
        var local = await CreateRoleAsync(_companyId, _appId, "Local", "LOCAL", parentRoleId: manager.RoleId);

        var response = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles/bulk-assign",
            new { RoleId = local, UserCompanyIds = new[] { foreignMembership.Id } });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await WithDbAsync(db => db.UserRoles.AnyAsync(ur => ur.RoleId == local))).ShouldBeFalse();
    }

    [Test]
    public async Task RoleTree_UnrelatedHolding_Forbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read");

        var response = await session.Client.GetAsync($"/api/v1/access-control/roles/tree?holdingId={_otherCompanyId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [TestCase(RoleKind.CompanySuperAdmin)]
    [TestCase(RoleKind.GlobalSuperAdmin)]
    public async Task CreateRole_SuperAdminKind_Forbidden(RoleKind kind)
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create");

        var response = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles",
            new { CompanyId = _companyId, ApplicationId = _appId, Code = "ADMIN", Name = "Admin", Kind = kind });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await WithDbAsync(db => db.BusinessRoles.AnyAsync(r => r.Kind != RoleKind.Standard))).ShouldBeFalse();
    }

    [TestCase(RoleKind.CompanySuperAdmin)]
    [TestCase(RoleKind.GlobalSuperAdmin)]
    public async Task UpdateRole_EscalateToSuperAdmin_Forbidden(RoleKind kind)
    {
        var local = await CreateRoleAsync(_companyId, _appId, "Local", "LOCAL");
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Edit");

        var response = await session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{local}",
            new { Name = "Local", Code = "LOCAL", Kind = kind, Status = RoleStatus.Active });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await WithDbAsync(db => db.BusinessRoles.AsNoTracking().Where(r => r.Id == local).Select(r => r.Kind).SingleAsync())).ShouldBe(RoleKind.Standard);
    }

    [Test]
    public async Task UpdateRole_ExistingSuperAdmin_Forbidden()
    {
        var admin = await CreateRoleAsync(_companyId, _appId, "Admin", "ADMIN", RoleKind.CompanySuperAdmin);
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Edit");

        var response = await session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{admin}",
            new { Name = "Renamed", Code = "ADMIN", Kind = RoleKind.Standard, Status = RoleStatus.Active });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await WithDbAsync(db => db.BusinessRoles.AsNoTracking().Where(r => r.Id == admin).Select(r => r.Kind).SingleAsync())).ShouldBe(RoleKind.CompanySuperAdmin);
    }

    [Test]
    public async Task GetPersonnel_OtherCompanyId_Forbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Read");

        var response = await session.Client.GetAsync($"/api/v1/organization/personnel/?companyId={_otherCompanyId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Personnel_ActiveOnlyInOtherCompany_NotVisible()
    {
        var foreignSession = await CreateAuthorizedClientAsync("foreign@test.com", _otherCompanyId,
            "Organization.Personnel.Create", "Organization.Position.Create", "Organization.PersonnelPosition.Create");
        var personnelId = await CreatePersonnelAsync(foreignSession, "5550001111");
        var foreignPosition = await CreatePositionAsync(foreignSession, _otherCompanyId, "FOREIGNPOS");
        await AssignAsync(foreignSession, personnelId, foreignPosition);

        var session = await CreateAuthorizedClientAsync(_companyId,
            "Organization.Personnel.Read", "Organization.Personnel.Edit", "Organization.Personnel.Delete");

        (await session.Client.GetAsync($"/api/v1/organization/personnel/{personnelId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var list = await session.Client.GetAsync("/api/v1/organization/personnel/");
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await list.Content.ReadAsStringAsync()).ShouldNotContain(personnelId.ToString());

        var update = await session.Client.PutAsJsonAsync($"/api/v1/organization/personnel/{personnelId}",
            new { FirstName = "Hacked", LastName = "Person", NationalCode = "5550001111", Gender = 1, Status = PersonnelStatus.Employed });
        update.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await session.Client.DeleteAsync($"/api/v1/organization/personnel/{personnelId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await WithDbAsync(db => db.Personnel.AsNoTracking().Where(p => p.Id == personnelId).Select(p => p.FirstName).SingleAsync())).ShouldBe("Test");
    }

    [Test]
    public async Task AssignPosition_OtherCompanyPosition_NotFound()
    {
        var foreignPosition = await SeedPositionAsync(_otherCompanyId, "FOREIGNPOS");
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Create", "Organization.PersonnelPosition.Create");
        var personnelId = await CreatePersonnelAsync(session, "5550002222");

        var response = await session.Client.PostAsJsonAsync($"/api/v1/organization/personnel/{personnelId}/positions",
            new { PositionId = foreignPosition, IsPrimary = false, EffectiveFrom = DateTime.UtcNow.AddDays(-1) });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await WithDbAsync(db => db.PersonnelPositions.AnyAsync(pp => pp.PersonnelId == personnelId))).ShouldBeFalse();
    }

    [Test]
    public async Task RemoveAssignment_InOtherCompany_NotFound()
    {
        var foreignSession = await CreateAuthorizedClientAsync("foreign@test.com", _otherCompanyId,
            "Organization.Personnel.Create", "Organization.Position.Create", "Organization.PersonnelPosition.Create");
        var personnelId = await CreatePersonnelAsync(foreignSession, "5550003333");
        var foreignPosition = await CreatePositionAsync(foreignSession, _otherCompanyId, "FOREIGNPOS");
        var assignmentId = await AssignAsync(foreignSession, personnelId, foreignPosition);

        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.PersonnelPosition.Create", "Organization.PersonnelPosition.Delete");

        var response = await session.Client.DeleteAsync($"/api/v1/organization/personnel/{personnelId}/positions/{assignmentId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await WithDbAsync(db => db.PersonnelPositions.AsNoTracking().Where(pp => pp.Id == assignmentId).Select(pp => pp.Status).SingleAsync()))
            .ShouldBe(PersonnelPositionStatus.Active);
    }

    private Task<Guid> SeedPositionAsync(Guid companyId, string code)
        => WithDbAsync(async db =>
        {
            var position = new Position(companyId, code, code);
            db.Positions.Add(position);
            await db.SaveChangesAsync(default);
            return position.Id;
        });

    private static async Task<Guid> CreatePositionAsync(AuthSession session, Guid companyId, string code)
    {
        var response = await session.Client.PostAsJsonAsync("/api/v1/organization/positions", new { CompanyId = companyId, Code = code, Title = code });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private static async Task<Guid> CreatePersonnelAsync(AuthSession session, string nationalCode)
    {
        var response = await session.Client.PostAsJsonAsync("/api/v1/organization/personnel",
            new { NationalCode = nationalCode, FirstName = "Test", LastName = "Person", Gender = 1 });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private static async Task<Guid> AssignAsync(AuthSession session, Guid personnelId, Guid positionId)
    {
        var response = await session.Client.PostAsJsonAsync($"/api/v1/organization/personnel/{personnelId}/positions",
            new { PositionId = positionId, IsPrimary = false, EffectiveFrom = DateTime.UtcNow.AddDays(-1) });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private record IdResponse(Guid Id);
}
