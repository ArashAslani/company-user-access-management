using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Models;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Organization;
using CompanyAccessManagement.Infrastructure.Data;
using CompanyAccessManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Shouldly;
using System.Net;
using System.Net.Http.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

[TestFixture]
public class RoleApiTests : ApiTestBase
{
    private Guid _companyId;
    private Guid _appId;
    private Guid _productsReadPermId;
    private Guid _productsEditPermId;
    private Guid _productsDeletePermId;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _appId, _productsReadPermId, _productsEditPermId, _productsDeletePermId) = await SeedTestDataAsync();
    }

    [Test]
    public async Task GetRoles_WithPermission_ReturnsOk()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read");

        var url = $"/api/v1/access-control/roles/?companyId={_companyId}";
        var absoluteUrl = new Uri(session.Client.BaseAddress!, url);
        
        var response = await session.Client.GetAsync(absoluteUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PaginatedList<RoleDto>>();
        result.ShouldNotBeNull();
        result.Items.ShouldNotBeNull();
    }

    [Test]
    public async Task GetRoles_WithoutPermission_ReturnsForbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId);

        var response = await session.Client.GetAsync($"/api/v1/access-control/roles/?companyId={_companyId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task CreateRole_WithValidData_ReturnsCreated()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create");

        var command = new
        {
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = "NEWROLE",
            Name = "New Role",
            Kind = RoleKind.Standard,
            ParentRoleId = (Guid?)null
        };

        var response = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles", command);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var result = await response.Content.ReadFromJsonAsync<CreateRoleResponse>();
        result.ShouldNotBeNull();
        result.Id.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task GetRole_ById_ReturnsOk()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create");

        var command = new
        {
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = "TESTROLE",
            Name = "Test Role",
            Kind = RoleKind.Standard,
            ParentRoleId = (Guid?)null
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles", command);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoleResponse>();
        var roleId = createResult!.Id;

        var response = await session.Client.GetAsync($"/api/v1/access-control/roles/{roleId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<RoleDetailDto>();
        result.ShouldNotBeNull();
        result.Code.ShouldBe("TESTROLE");
    }

    [Test]
    public async Task UpdateRole_WithValidData_ReturnsOk()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Edit", "AccessManagement.Role.Create");

        var command = new
        {
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = "TESTROLE",
            Name = "Test Role",
            Kind = RoleKind.Standard,
            ParentRoleId = (Guid?)null
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles", command);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoleResponse>();
        var roleId = createResult!.Id;

        var updateCommand = new
        {
            Id = roleId,
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = "TESTROLE",
            Name = "Updated Role",
            Kind = RoleKind.Standard,
            ParentRoleId = (Guid?)null
        };

        var response = await session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{roleId}", updateCommand);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        await WithDbAsync(async db =>
        {
            var updated = await db.BusinessRoles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == roleId);
            updated!.Name.ShouldBe("Updated Role");
        });
    }

    [Test]
    public async Task DeleteRole_WithNoAssignments_ReturnsNoContent()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Delete", "AccessManagement.Role.Create");

        var command = new
        {
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = "TESTROLE",
            Name = "Test Role",
            Kind = RoleKind.Standard,
            ParentRoleId = (Guid?)null
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles", command);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoleResponse>();
        var roleId = createResult!.Id;

        var response = await session.Client.DeleteAsync($"/api/v1/access-control/roles/{roleId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task GetRoleTree_ReturnsTree()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create");

        var parentCommand = new
        {
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = "PARENT",
            Name = "Parent Role",
            Kind = RoleKind.Standard,
            ParentRoleId = (Guid?)null
        };

        var parentResponse = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles", parentCommand);
        parentResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var parentResult = await parentResponse.Content.ReadFromJsonAsync<CreateRoleResponse>();
        var parentId = parentResult!.Id;

        var childCommand = new
        {
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = "CHILD",
            Name = "Child Role",
            Kind = RoleKind.Standard,
            ParentRoleId = parentId
        };

        var childResponse = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles", childCommand);
        childResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await session.Client.GetAsync($"/api/v1/access-control/roles/tree?holdingId={_companyId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<RoleTreeDto>();
        result.ShouldNotBeNull();
    }

    [Test]
    public async Task UpdateRolePermissions_WithValidData_ReturnsOk()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Permissions.Manage", "AccessManagement.Role.Create");
        var manager = await CreateManagingRoleAsync(session, _companyId, "MANAGER", ScopeMode.All, _productsReadPermId);

        var roleCommand = new
        {
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = "TESTROLE",
            Name = "Test Role",
            Kind = RoleKind.Standard,
            ParentRoleId = (Guid?)manager.RoleId
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles", roleCommand);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoleResponse>();
        var roleId = createResult!.Id;

        var permCommand = new
        {
            RoleId = roleId,
            PermissionIds = new Guid[0]
        };

        var response = await session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{roleId}/permissions", permCommand);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task CreateRole_CreatesRoleAuthPrincipal()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create");

        var roleId = await CreateRoleViaApiAsync(session, "WITHPRINCIPAL");

        var principals = await WithDbAsync(db => db.AuthPrincipals.AsNoTracking()
            .Where(ap => ap.Type == PrincipalType.Role && ap.ReferenceId == roleId)
            .ToListAsync());
        principals.Count.ShouldBe(1);
        principals[0].CompanyId.ShouldBe(_companyId);
        principals[0].ApplicationId.ShouldBe(_appId);
    }

    [Test]
    public async Task UpdateRolePermissions_CalledTwice_DoesNotDuplicateRules()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create", "AccessManagement.Role.Permissions.Manage");
        var manager = await CreateManagingRoleAsync(session, _companyId, "MANAGER", ScopeMode.All, _productsReadPermId);
        var roleId = await CreateRoleViaApiAsync(session, "IDEMPOTENT", manager.RoleId);
        var productsId = await GetProductsResourceIdAsync();

        var scoped = PermissionsBody(productsId, ("Read", AccessEffect.Allow, "Workshop", ["A", "B"]));
        (await session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{roleId}/permissions", scoped)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{roleId}/permissions", scoped)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var rules = await GetRoleRulesAsync(roleId);
        rules.Count.ShouldBe(1);
        rules[0].PermissionId.ShouldBe(_productsReadPermId);
        rules[0].ScopeMode.ShouldBe(ScopeMode.Selected);
        rules[0].Scopes.Select(s => s.ScopeKey).OrderBy(k => k).ShouldBe(["A", "B"]);

        var unscoped = PermissionsBody(productsId, ("Read", AccessEffect.Allow, null, null));
        (await session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{roleId}/permissions", unscoped)).StatusCode.ShouldBe(HttpStatusCode.OK);

        rules = await GetRoleRulesAsync(roleId);
        rules.Count.ShouldBe(1);
        rules[0].ScopeMode.ShouldBe(ScopeMode.None);
        rules[0].Scopes.ShouldBeEmpty();
    }

    [Test]
    public async Task UpdateRolePermissions_UnknownAction_ReturnsNotFound()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create", "AccessManagement.Role.Permissions.Manage");
        var roleId = await CreateRoleViaApiAsync(session, "UNKNOWNACTION");
        var productsId = await GetProductsResourceIdAsync();

        var response = await session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{roleId}/permissions",
            PermissionsBody(productsId, ("DoesNotExist", AccessEffect.Allow, null, null)));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetRoleRulesAsync(roleId)).ShouldBeEmpty();
    }

    [Test]
    public async Task CopyRolePermissions_CopiesSourceRoleRulesIntoTarget()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create", "AccessManagement.Role.Permissions.Manage");
        var manager = await CreateManagingRoleAsync(session, _companyId, "MANAGER", ScopeMode.All, _productsReadPermId, _productsEditPermId);
        var sourceId = await CreateRoleViaApiAsync(session, "SOURCE", manager.RoleId);
        var targetId = await CreateRoleViaApiAsync(session, "TARGET", manager.RoleId);
        var productsId = await GetProductsResourceIdAsync();

        var body = PermissionsBody(productsId, ("Read", AccessEffect.Allow, "Workshop", ["A"]), ("Edit", AccessEffect.Deny, null, null));
        (await session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{sourceId}/permissions", body)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var first = await session.Client.PostAsJsonAsync($"/api/v1/access-control/roles/{targetId}/permissions/copy-from", new { SourceRoleId = sourceId, Mode = "APPEND" });
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await first.Content.ReadFromJsonAsync<CopyResponse>())!.CopiedAccessRuleCount.ShouldBe(2);

        var second = await session.Client.PostAsJsonAsync($"/api/v1/access-control/roles/{targetId}/permissions/copy-from", new { SourceRoleId = sourceId, Mode = "APPEND" });
        (await second.Content.ReadFromJsonAsync<CopyResponse>())!.CopiedAccessRuleCount.ShouldBe(0);

        var targetRules = await GetRoleRulesAsync(targetId);
        targetRules.Select(r => (r.PermissionId, r.Effect)).OrderBy(x => x.Effect)
            .ShouldBe([(_productsReadPermId, AccessEffect.Allow), (_productsEditPermId, AccessEffect.Deny)]);
        targetRules.Single(r => r.Effect == AccessEffect.Allow).Scopes.Single().ScopeKey.ShouldBe("A");
        (await GetRoleRulesAsync(sourceId)).Count.ShouldBe(2);
    }

    [Test]
    public async Task CopyRolePermissions_Replace_RemovesTargetRulesNotInSource()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create", "AccessManagement.Role.Permissions.Manage");
        var manager = await CreateManagingRoleAsync(session, _companyId, "MANAGER", ScopeMode.All, _productsReadPermId, _productsDeletePermId);
        var sourceId = await CreateRoleViaApiAsync(session, "SOURCE", manager.RoleId);
        var targetId = await CreateRoleViaApiAsync(session, "TARGET", manager.RoleId);
        var productsId = await GetProductsResourceIdAsync();

        (await session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{sourceId}/permissions", PermissionsBody(productsId, ("Read", AccessEffect.Allow, null, null)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{targetId}/permissions", PermissionsBody(productsId, ("Delete", AccessEffect.Allow, null, null)))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await session.Client.PostAsJsonAsync($"/api/v1/access-control/roles/{targetId}/permissions/copy-from", new { SourceRoleId = sourceId, Mode = "REPLACE" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var targetRules = await GetRoleRulesAsync(targetId);
        targetRules.Select(r => r.PermissionId).ShouldBe([_productsReadPermId]);
    }

    [Test]
    public async Task CopyRolePermissions_FromItself_ReturnsConflict()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create", "AccessManagement.Role.Permissions.Manage");
        var roleId = await CreateRoleViaApiAsync(session, "SELF");

        var response = await session.Client.PostAsJsonAsync($"/api/v1/access-control/roles/{roleId}/permissions/copy-from", new { SourceRoleId = roleId, Mode = "REPLACE" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Role_SelfParent_Denied()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create", "AccessManagement.Role.Edit");
        var a = await CreateRoleViaApiAsync(session, "A");

        var response = await UpdateRoleParentAsync(session, a, "A", a);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(response)).ShouldBe("HIERARCHY_CYCLE");
    }

    [Test]
    public async Task Role_CrossCompanyParent_Denied()
    {
        var otherCompanyId = await CreateCompanyAsync("OTHER", "Other Company");
        var otherRoleId = await WithDbAsync(async db =>
        {
            var role = new Role(otherCompanyId, _appId, "OTHER", "Other");
            db.BusinessRoles.Add(role);
            db.AuthPrincipals.Add(AuthPrincipal.ForRole(role.Id, otherCompanyId, _appId));
            await db.SaveChangesAsync(default);
            return role.Id;
        });

        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create", "AccessManagement.Role.Edit");
        var a = await CreateRoleViaApiAsync(session, "A");

        var response = await UpdateRoleParentAsync(session, a, "A", otherRoleId);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await WithDbAsync(db => db.BusinessRoles.AsNoTracking().Where(r => r.Id == a).Select(r => r.ParentRoleId).SingleAsync())).ShouldBeNull();
    }

    [Test]
    public async Task Role_CrossApplicationParent_Denied()
    {
        var otherAppId = await CreateApplicationAsync("OTHERAPP", "Other App");
        var otherRoleId = await WithDbAsync(async db =>
        {
            var role = new Role(_companyId, otherAppId, "OTHER", "Other");
            db.BusinessRoles.Add(role);
            db.AuthPrincipals.Add(AuthPrincipal.ForRole(role.Id, _companyId, otherAppId));
            await db.SaveChangesAsync(default);
            return role.Id;
        });

        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create", "AccessManagement.Role.Edit");
        var a = await CreateRoleViaApiAsync(session, "A");

        var response = await UpdateRoleParentAsync(session, a, "A", otherRoleId);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(response)).ShouldBe("ROLE_PARENT_APPLICATION_MISMATCH");
    }

    [Test]
    public async Task Role_A_B_C_SetAParentC_Denied()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create", "AccessManagement.Role.Edit");
        var a = await CreateRoleViaApiAsync(session, "A");
        var b = await CreateRoleViaApiAsync(session, "B", a);
        var c = await CreateRoleViaApiAsync(session, "C", b);

        var response = await UpdateRoleParentAsync(session, a, "A", c);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(response)).ShouldBe("HIERARCHY_CYCLE");
    }

    [Test]
    public async Task Role_LegalReparent_Allowed()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create", "AccessManagement.Role.Edit");
        var a = await CreateRoleViaApiAsync(session, "A");
        var b = await CreateRoleViaApiAsync(session, "B", a);
        var c = await CreateRoleViaApiAsync(session, "C");

        var response = await UpdateRoleParentAsync(session, b, "B", c);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await WithDbAsync(async db =>
        {
            var updated = await db.BusinessRoles.AsNoTracking().SingleAsync(r => r.Id == b);
            updated.ParentRoleId.ShouldBe(c);
        });
    }

    private async Task<Guid> CreateRoleViaApiAsync(AuthSession session, string code, Guid? parentRoleId = null)
    {
        var response = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles", new
        {
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = code,
            Name = code,
            Kind = RoleKind.Standard,
            ParentRoleId = parentRoleId
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreateRoleResponse>())!.Id;
    }

    private Task<HttpResponseMessage> UpdateRoleParentAsync(AuthSession session, Guid id, string code, Guid? parentRoleId)
        => session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{id}", new
        {
            Name = code,
            Code = code,
            Kind = RoleKind.Standard,
            ParentRoleId = parentRoleId,
            Status = RoleStatus.Active
        });

    private Task<Guid> GetProductsResourceIdAsync()
        => WithDbAsync(db => db.Resources.Where(r => r.ApplicationId == _appId && r.Code == "Products").Select(r => r.Id).SingleAsync());

    private Task<List<AccessRule>> GetRoleRulesAsync(Guid roleId)
        => WithDbAsync(db => (from ap in db.AuthPrincipals
                              join ar in db.AccessRules.Include(r => r.Scopes) on ap.Id equals ar.PrincipalId
                              where ap.Type == PrincipalType.Role && ap.ReferenceId == roleId
                              select ar).AsNoTracking().ToListAsync());

    private static object PermissionsBody(Guid resourceId, params (string ActionCode, AccessEffect Effect, string? ScopeType, string[]? ScopeKeys)[] actions)
        => new
        {
            Entries = new[]
            {
                new
                {
                    ResourceId = resourceId,
                    Actions = actions.Select(a => new { a.ActionCode, a.Effect, a.ScopeType, a.ScopeKeys }).ToArray()
                }
            }
        };

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString()
            : doc.RootElement.TryGetProperty("extensions", out var ext) && ext.TryGetProperty("code", out var nested)
                ? nested.GetString()
                : null;
    }

    private record CreateRoleResponse(Guid Id);

    private record CopyResponse(int CopiedAccessRuleCount);
}