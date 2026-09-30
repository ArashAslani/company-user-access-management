using CompanyAccessManagement.Application.Common.Models;
using CompanyAccessManagement.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;
using System.Net;
using System.Net.Http.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

/// <summary>
/// Administration is locked to the QC application: roles and resources of any other application answer 404
/// and are never changed, even for an actor holding every QC administration permission.
/// </summary>
[TestFixture]
public class ApplicationBoundaryApiTests : ApiTestBase
{
    private static readonly string[] AdminPermissions =
    [
        "AccessManagement.Role.Read",
        "AccessManagement.Role.Create",
        "AccessManagement.Role.Edit",
        "AccessManagement.Role.Delete",
        "AccessManagement.Role.Permissions.Manage",
        "AccessManagement.Role.BulkAssign",
        "AccessManagement.Resource.Read"
    ];

    private Guid _companyId;
    private Guid _qcAppId;
    private Guid _otherAppId;
    private Guid _otherResourceId;
    private Guid _otherRoleId;
    private Guid _otherRolePrincipalId;
    private Guid _otherRuleId;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _qcAppId, _, _, _) = await SeedTestDataAsync();

        _otherAppId = await CreateApplicationAsync("HR", "Human Resources");
        _otherResourceId = await CreateResourceAsync(_otherAppId, "Payroll", "Payroll");
        var otherPermissionId = await CreatePermissionAsync(_otherResourceId, "Read");
        await CreatePermissionAsync(_otherResourceId, "Approve");

        _otherRoleId = await CreateRoleAsync(_companyId, _otherAppId, "Payroll Clerk", "PAYROLL");
        _otherRolePrincipalId = await WithDbAsync(db => db.AuthPrincipals
            .Where(ap => ap.Type == PrincipalType.Role && ap.ReferenceId == _otherRoleId)
            .Select(ap => ap.Id)
            .SingleAsync());
        _otherRuleId = (await CreateAccessRuleAsync(_otherRolePrincipalId, otherPermissionId, AccessEffect.Allow)).Id;
    }

    [Test]
    public async Task CreateRole_InOtherApplication_ReturnsNotFoundAndCreatesNothing()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, AdminPermissions);

        var response = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles",
            new { CompanyId = _companyId, ApplicationId = _otherAppId, Code = "HR_NEW", Name = "HR New", Kind = RoleKind.Standard });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await WithDbAsync(db => db.BusinessRoles.AnyAsync(r => r.Code == "HR_NEW"))).ShouldBeFalse();
    }

    [Test]
    public async Task ResourceTree_OfOtherApplication_ReturnsNotFound()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, AdminPermissions);

        var other = await session.Client.GetAsync($"/api/v1/access-control/scopes/resources/tree?applicationId={_otherAppId}");
        var qc = await session.Client.GetAsync($"/api/v1/access-control/scopes/resources/tree?applicationId={_qcAppId}");

        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        qc.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task UpdateRolePermissions_OnOtherApplicationRole_ReturnsNotFoundAndLeavesRulesUnchanged()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, AdminPermissions);

        var response = await session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{_otherRoleId}/permissions", new
        {
            Entries = new[]
            {
                new { ResourceId = _otherResourceId, Actions = new[] { new { ActionCode = "Approve", Effect = AccessEffect.Allow } } }
            }
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var ruleIds = await WithDbAsync(db => db.AccessRules
            .Where(ar => ar.PrincipalId == _otherRolePrincipalId)
            .Select(ar => ar.Id)
            .ToListAsync());
        ruleIds.ShouldBe([_otherRuleId]);
    }

    [Test]
    public async Task OtherApplicationRole_IsInvisibleToEveryRoleEndpoint()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, AdminPermissions);
        var qcRoleId = await CreateRoleAsync(_companyId, _qcAppId, "QC Role", "QC_ROLE");
        var userCompanyId = await WithDbAsync(db => db.UserCompanies
            .Where(uc => uc.PrincipalId == session.PrincipalId)
            .Select(uc => uc.Id)
            .SingleAsync());

        (await session.Client.GetAsync($"/api/v1/access-control/roles/{_otherRoleId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{_otherRoleId}",
            new { Code = "PAYROLL", Name = "Renamed", Kind = RoleKind.Standard, Status = RoleStatus.Active }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await session.Client.DeleteAsync($"/api/v1/access-control/roles/{_otherRoleId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await session.Client.PostAsJsonAsync($"/api/v1/access-control/roles/{_otherRoleId}/permissions/copy-from",
            new { SourceRoleId = qcRoleId, Mode = "APPEND" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await session.Client.PostAsJsonAsync("/api/v1/access-control/roles/bulk-assign",
            new { RoleId = _otherRoleId, UserCompanyIds = new[] { userCompanyId } }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var list = await session.Client.GetFromJsonAsync<PaginatedList<RoleDto>>("/api/v1/access-control/roles/");
        list!.Items.Select(r => r.Id).ShouldContain(qcRoleId);
        list.Items.Select(r => r.Id).ShouldNotContain(_otherRoleId);

        var tree = await session.Client.GetStringAsync($"/api/v1/access-control/roles/tree?holdingId={_companyId}");
        tree.ShouldContain(qcRoleId.ToString());
        tree.ShouldNotContain(_otherRoleId.ToString());

        var stored = await WithDbAsync(db => db.BusinessRoles.Include(r => r.UserRoles).SingleAsync(r => r.Id == _otherRoleId));
        stored.Name.ShouldBe("Payroll Clerk");
        stored.Status.ShouldBe(RoleStatus.Active);
        stored.UserRoles.ShouldBeEmpty();
    }
}
