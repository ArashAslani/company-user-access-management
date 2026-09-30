using CompanyAccessManagement.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;
using System.Net;
using System.Net.Http.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

/// <summary>
/// Admin Authority (design §32) on the grant paths. The actor always holds the endpoint permission; what varies is whether
/// one single managing role covers the target and every requested grant.
/// </summary>
[TestFixture]
public class AdminAuthorityApiTests : ApiTestBase
{
    private static readonly string[] GrantPermissions =
    [
        "AccessManagement.Role.Read",
        "AccessManagement.Role.Permissions.Manage",
        "AccessManagement.Role.BulkAssign"
    ];

    private Guid _companyId;
    private Guid _appId;
    private Guid _readPermId;
    private Guid _editPermId;
    private Guid _deletePermId;
    private Guid _productsId;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _appId, _readPermId, _editPermId, _deletePermId) = await SeedTestDataAsync();
        _productsId = await WithDbAsync(db => db.Resources.Where(r => r.ApplicationId == _appId && r.Code == "Products").Select(r => r.Id).SingleAsync());
    }

    [Test]
    public async Task NoManagingRole_Forbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, GrantPermissions);
        var target = await CreateRoleAsync(_companyId, _appId, "Target", "TARGET");

        var response = await PutPermissionsAsync(session, target, ("Read", AccessEffect.Allow, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RulesOfAsync(target)).ShouldBeEmpty();
    }

    [Test]
    public async Task TargetOutsideSubtree_Forbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, GrantPermissions);
        await CreateManagingRoleAsync(session, _companyId, "MANAGER", ScopeMode.All, _readPermId);
        var sibling = await CreateRoleAsync(_companyId, _appId, "Sibling", "SIBLING");

        var response = await PutPermissionsAsync(session, sibling, ("Read", AccessEffect.Allow, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RulesOfAsync(sibling)).ShouldBeEmpty();
    }

    [Test]
    public async Task TargetIsManagingRoleItself_Forbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, GrantPermissions);
        var manager = await CreateManagingRoleAsync(session, _companyId, "MANAGER", ScopeMode.All, _readPermId);

        var response = await PutPermissionsAsync(session, manager.RoleId, ("Edit", AccessEffect.Allow, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RulesOfAsync(manager.RoleId)).Select(r => r.PermissionId).ShouldBe([_readPermId]);
    }

    [Test]
    public async Task ManagingRoleLacksPermission_Forbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, GrantPermissions);
        var manager = await CreateManagingRoleAsync(session, _companyId, "MANAGER", ScopeMode.All, _readPermId);
        var target = await CreateRoleAsync(_companyId, _appId, "Target", "TARGET", parentRoleId: manager.RoleId);

        var response = await PutPermissionsAsync(session, target, ("Delete", AccessEffect.Allow, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RulesOfAsync(target)).ShouldBeEmpty();
    }

    [Test]
    public async Task DenyEntry_RequiresSameAuthorityAsAllow()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, GrantPermissions);
        var manager = await CreateManagingRoleAsync(session, _companyId, "MANAGER", ScopeMode.All, _readPermId);
        var target = await CreateRoleAsync(_companyId, _appId, "Target", "TARGET", parentRoleId: manager.RoleId);

        var response = await PutPermissionsAsync(session, target, ("Delete", AccessEffect.Deny, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RulesOfAsync(target)).ShouldBeEmpty();
    }

    [Test]
    public async Task DenyOnManagingBranch_Forbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, GrantPermissions);
        var boundary = await CreateRoleAsync(_companyId, _appId, "Boundary", "BOUNDARY");
        await CreateAccessRuleAsync(await RolePrincipalAsync(boundary), _readPermId, AccessEffect.Deny, ScopeMode.All);
        var manager = await CreateManagingRoleAsync(session, _companyId, "MANAGER", ScopeMode.All, _readPermId);
        await WithDbAsync(async db =>
        {
            (await db.BusinessRoles.SingleAsync(r => r.Id == manager.RoleId)).ChangeParent(boundary);
            await db.SaveChangesAsync(default);
        });
        var target = await CreateRoleAsync(_companyId, _appId, "Target", "TARGET", parentRoleId: manager.RoleId);

        var response = await PutPermissionsAsync(session, target, ("Read", AccessEffect.Allow, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RulesOfAsync(target)).ShouldBeEmpty();
    }

    [Test]
    public async Task ActorDirectDeny_AppliesGlobally()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, GrantPermissions);
        await CreateAccessRuleAsync(session.PrincipalId, _readPermId, AccessEffect.Deny, ScopeMode.All);
        var manager = await CreateManagingRoleAsync(session, _companyId, "MANAGER", ScopeMode.All, _readPermId);
        var target = await CreateRoleAsync(_companyId, _appId, "Target", "TARGET", parentRoleId: manager.RoleId);

        var response = await PutPermissionsAsync(session, target, ("Read", AccessEffect.Allow, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RulesOfAsync(target)).ShouldBeEmpty();
    }

    [Test]
    public async Task RequestedScopeWiderThanManagingScope_Forbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, GrantPermissions);
        var manager = await CreateManagingRoleAsync(session, _companyId, "MANAGER", ScopeMode.None);
        await AddSelectedRuleAsync(manager.PrincipalId, _readPermId, "Workshop", "A");
        var target = await CreateRoleAsync(_companyId, _appId, "Target", "TARGET", parentRoleId: manager.RoleId);

        var wider = await PutPermissionsAsync(session, target, ("Read", AccessEffect.Allow, "Workshop", ["A", "B"]));
        var unscoped = await PutPermissionsAsync(session, target, ("Read", AccessEffect.Allow, null, null));

        wider.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        unscoped.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RulesOfAsync(target)).ShouldBeEmpty();
    }

    [Test]
    public async Task ScopeInsideManagingScope_Allowed()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, GrantPermissions);
        var manager = await CreateManagingRoleAsync(session, _companyId, "MANAGER", ScopeMode.None);
        await AddSelectedRuleAsync(manager.PrincipalId, _readPermId, "Workshop", "A", "B");
        var target = await CreateRoleAsync(_companyId, _appId, "Target", "TARGET", parentRoleId: manager.RoleId);

        var response = await PutPermissionsAsync(session, target, ("Read", AccessEffect.Allow, "Workshop", ["A"]));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rule = (await RulesOfAsync(target)).ShouldHaveSingleItem();
        rule.PermissionId.ShouldBe(_readPermId);
        rule.Scopes.Select(s => s.ScopeKey).ShouldBe(["A"]);
    }

    [Test]
    public async Task GrandchildTarget_AllowedThroughManagingSubtree()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, GrantPermissions);
        var manager = await CreateManagingRoleAsync(session, _companyId, "MANAGER", ScopeMode.All, _readPermId, _editPermId);
        var child = await CreateRoleAsync(_companyId, _appId, "Child", "CHILD", parentRoleId: manager.RoleId);
        var grandchild = await CreateRoleAsync(_companyId, _appId, "Grandchild", "GRANDCHILD", parentRoleId: child);

        var response = await PutPermissionsAsync(session, grandchild, ("Edit", AccessEffect.Allow, null, null), ("Read", AccessEffect.Allow, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RulesOfAsync(grandchild)).Select(r => r.PermissionId).OrderBy(id => id).ShouldBe(new[] { _readPermId, _editPermId }.OrderBy(id => id));
    }

    [Test]
    public async Task AuthorityIsNotCombinedAcrossRoles_Forbidden()
    {
        // One role holds the permission but not the target; the other holds the target but not the permission.
        // The actor's own direct ALLOW does not count either: only a managing role's branch confers authority.
        var session = await CreateAuthorizedClientAsync(_companyId, [.. GrantPermissions, "Products.Delete"]);
        await CreateManagingRoleAsync(session, _companyId, "HAS_PERMISSION", ScopeMode.All, _readPermId, _deletePermId);
        var treeOwner = await CreateManagingRoleAsync(session, _companyId, "HAS_TARGET", ScopeMode.All, _readPermId);
        var target = await CreateRoleAsync(_companyId, _appId, "Target", "TARGET", parentRoleId: treeOwner.RoleId);

        var response = await PutPermissionsAsync(session, target, ("Delete", AccessEffect.Allow, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RulesOfAsync(target)).ShouldBeEmpty();
    }

    [Test]
    public async Task ScopeIsNotCombinedAcrossRoles_Forbidden()
    {
        // OUTER -> GAP (inactive) -> INNER -> TARGET. Both held roles manage TARGET, but the inactive GAP stops Role Up,
        // so OUTER covers only Workshop A and INNER only Workshop B.
        var session = await CreateAuthorizedClientAsync(_companyId, GrantPermissions);
        var outer = await CreateManagingRoleAsync(session, _companyId, "OUTER", ScopeMode.None);
        await AddSelectedRuleAsync(outer.PrincipalId, _readPermId, "Workshop", "A");
        var gap = await CreateRoleAsync(_companyId, _appId, "Gap", "GAP", parentRoleId: outer.RoleId);
        var inner = await CreateManagingRoleAsync(session, _companyId, "INNER", ScopeMode.None);
        await AddSelectedRuleAsync(inner.PrincipalId, _readPermId, "Workshop", "B");
        await WithDbAsync(async db =>
        {
            (await db.BusinessRoles.SingleAsync(r => r.Id == gap)).SetStatus(RoleStatus.Inactive);
            (await db.BusinessRoles.SingleAsync(r => r.Id == inner.RoleId)).ChangeParent(gap);
            await db.SaveChangesAsync(default);
        });
        var target = await CreateRoleAsync(_companyId, _appId, "Target", "TARGET", parentRoleId: inner.RoleId);

        var combined = await PutPermissionsAsync(session, target, ("Read", AccessEffect.Allow, "Workshop", ["A", "B"]));

        combined.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RulesOfAsync(target)).ShouldBeEmpty();

        (await PutPermissionsAsync(session, target, ("Read", AccessEffect.Allow, "Workshop", ["A"]))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PutPermissionsAsync(session, target, ("Read", AccessEffect.Allow, "Workshop", ["B"]))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task CompanySuperAdmin_BypassesManagingRoleCheck()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, GrantPermissions);
        var superAdmin = await CreateRoleAsync(_companyId, _appId, "Super", "SUPER", RoleKind.CompanySuperAdmin);
        await AssignAsync(session, superAdmin);
        var target = await CreateRoleAsync(_companyId, _appId, "Target", "TARGET");

        var response = await PutPermissionsAsync(session, target, ("Delete", AccessEffect.Allow, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RulesOfAsync(target)).Select(r => r.PermissionId).ShouldBe([_deletePermId]);
    }

    [Test]
    public async Task Copy_RuleOutsideManagingScope_ForbiddenAndTargetUnchanged()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, GrantPermissions);
        var manager = await CreateManagingRoleAsync(session, _companyId, "MANAGER", ScopeMode.All, _readPermId);
        // Outside the manager's subtree: a descendant's rules would count towards the manager's branch (Role Up).
        var source = await CreateRoleAsync(_companyId, _appId, "Source", "SOURCE");
        var target = await CreateRoleAsync(_companyId, _appId, "Target", "TARGET", parentRoleId: manager.RoleId);
        await CreateAccessRuleAsync(await RolePrincipalAsync(source), _readPermId, AccessEffect.Allow);
        await CreateAccessRuleAsync(await RolePrincipalAsync(source), _deletePermId, AccessEffect.Allow);
        await CreateAccessRuleAsync(await RolePrincipalAsync(target), _readPermId, AccessEffect.Allow);

        var response = await session.Client.PostAsJsonAsync($"/api/v1/access-control/roles/{target}/permissions/copy-from",
            new { SourceRoleId = source, Mode = "REPLACE" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RulesOfAsync(target)).Select(r => r.PermissionId).ShouldBe([_readPermId]);
    }

    [Test]
    public async Task Copy_TargetOutsideSubtree_Forbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, GrantPermissions);
        var manager = await CreateManagingRoleAsync(session, _companyId, "MANAGER", ScopeMode.All, _readPermId);
        var source = await CreateRoleAsync(_companyId, _appId, "Source", "SOURCE", parentRoleId: manager.RoleId);
        var outside = await CreateRoleAsync(_companyId, _appId, "Outside", "OUTSIDE");
        await CreateAccessRuleAsync(await RolePrincipalAsync(source), _readPermId, AccessEffect.Allow);

        var response = await session.Client.PostAsJsonAsync($"/api/v1/access-control/roles/{outside}/permissions/copy-from",
            new { SourceRoleId = source, Mode = "APPEND" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RulesOfAsync(outside)).ShouldBeEmpty();
    }

    [Test]
    public async Task BulkAssign_OutsideSubtree_ForbiddenAndInsideAllowed()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, GrantPermissions);
        var manager = await CreateManagingRoleAsync(session, _companyId, "MANAGER", ScopeMode.None);
        var inside = await CreateRoleAsync(_companyId, _appId, "Inside", "INSIDE", parentRoleId: manager.RoleId);
        var outside = await CreateRoleAsync(_companyId, _appId, "Outside", "OUTSIDE");
        var member = await CreateUserAsync("member@test.com", "Test123!");
        var membership = await CreateUserCompanyAsync(member.Id, _companyId, Guid.Empty);

        var denied = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles/bulk-assign",
            new { RoleId = outside, UserCompanyIds = new[] { membership.Id } });
        var selfAssign = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles/bulk-assign",
            new { RoleId = manager.RoleId, UserCompanyIds = new[] { membership.Id } });
        var allowed = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles/bulk-assign",
            new { RoleId = inside, UserCompanyIds = new[] { membership.Id } });

        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        selfAssign.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await WithDbAsync(db => db.UserRoles.Where(ur => ur.UserCompanyId == membership.Id).Select(ur => ur.RoleId).ToListAsync()))
            .ShouldBe([inside]);
    }

    private Task<HttpResponseMessage> PutPermissionsAsync(AuthSession session, Guid roleId, params (string ActionCode, AccessEffect Effect, string? ScopeType, string[]? ScopeKeys)[] actions)
        => session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{roleId}/permissions", new
        {
            Entries = new[]
            {
                new { ResourceId = _productsId, Actions = actions.Select(a => new { a.ActionCode, a.Effect, a.ScopeType, a.ScopeKeys }).ToArray() }
            }
        });

    private Task<Guid> RolePrincipalAsync(Guid roleId)
        => WithDbAsync(db => db.AuthPrincipals.Where(ap => ap.Type == PrincipalType.Role && ap.ReferenceId == roleId).Select(ap => ap.Id).SingleAsync());

    private Task<List<AccessRule>> RulesOfAsync(Guid roleId)
        => WithDbAsync(db => (from ap in db.AuthPrincipals
                              join ar in db.AccessRules.Include(r => r.Scopes) on ap.Id equals ar.PrincipalId
                              where ap.Type == PrincipalType.Role && ap.ReferenceId == roleId
                              select ar).AsNoTracking().ToListAsync());

    private Task AddSelectedRuleAsync(Guid principalId, Guid permissionId, string scopeType, params string[] scopeKeys)
        => WithDbAsync(async db =>
        {
            var rule = new AccessRule(principalId, permissionId, AccessEffect.Allow, AccessRuleOrigin.Manual, ScopeMode.Selected);
            foreach (var key in scopeKeys)
                rule.AddScope(scopeType, key);
            db.AccessRules.Add(rule);
            await db.SaveChangesAsync(default);
        });

    private Task AssignAsync(AuthSession session, Guid roleId)
        => WithDbAsync(async db =>
        {
            var userCompanyId = await db.UserCompanies.Where(uc => uc.PrincipalId == session.PrincipalId).Select(uc => uc.Id).SingleAsync();
            db.UserRoles.Add(new UserRole(userCompanyId, roleId));
            await db.SaveChangesAsync(default);
        });
}
