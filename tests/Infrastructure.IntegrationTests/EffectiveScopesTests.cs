using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Infrastructure.Authorization;
using CompanyAccessManagement.Infrastructure.Data;
using CompanyAccessManagement.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace CompanyAccessManagement.IntegrationTests;

/// <summary>
/// Effective scope sets must agree with <see cref="IAccessEvaluator.EvaluateAsync"/> for every key in <see cref="Universe"/>.
/// </summary>
[TestFixture]
public class EffectiveScopesTests : TestBase
{
    private static readonly string[] Universe = ["F", "V", "S", "E"];

    private Guid _companyId;
    private Guid _appId;
    private Guid _readPermId;
    private Guid _editPermId;
    private Guid _userId;
    private Guid _principalId;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _appId, _readPermId, _editPermId, _) = await SeedTestDataAsync();
        (_userId, _principalId) = await CreateMemberAsync("scopes@test.com");
    }

    [Test]
    public async Task SelectedKeys_AreExactlyThoseKeys()
    {
        await AddRuleAsync(_principalId, _readPermId, AccessEffect.Allow, ScopeMode.Selected, "V", "F");

        var result = await ScopesAsync();

        result.ScopeMode.ShouldBe(ScopeMode.Selected);
        result.Keys.ShouldBe(["F", "V"]);
        await AssertInvariantAsync();
    }

    [Test]
    public async Task ScopeAll_ReturnsAll()
    {
        await AddRuleAsync(_principalId, _readPermId, AccessEffect.Allow, ScopeMode.All);

        var result = await ScopesAsync();

        result.ScopeMode.ShouldBe(ScopeMode.All);
        result.Keys.ShouldBeEmpty();
        await AssertInvariantAsync();
    }

    [Test]
    public async Task IndependentRoles_UnionSelectedKeys()
    {
        var roleA = await CreateGrantedRoleAsync("ROLE_A", AccessEffect.Allow, null, "F");
        var roleB = await CreateGrantedRoleAsync("ROLE_B", AccessEffect.Allow, null, "V");
        await AssignAsync(roleA, roleB);

        var result = await ScopesAsync();

        result.ScopeMode.ShouldBe(ScopeMode.Selected);
        result.Keys.ShouldBe(["F", "V"]);
        await AssertInvariantAsync();
    }

    [Test]
    public async Task BranchLocalDeny_DoesNotRemoveAnotherRolesKeys()
    {
        var roleA = await CreateGrantedRoleAsync("ROLE_A", AccessEffect.Deny, null, "V");
        var roleB = await CreateGrantedRoleAsync("ROLE_B", AccessEffect.Allow, null, "F", "V");
        await AssignAsync(roleA, roleB);

        var result = await ScopesAsync();

        result.ScopeMode.ShouldBe(ScopeMode.Selected);
        result.Keys.ShouldBe(["F", "V"]);
        await AssertInvariantAsync();
    }

    [Test]
    public async Task AncestorDeny_RemovesTheKeyFromEveryBranch()
    {
        var (parent, parentPrincipal) = await RoleAsync("PARENT");
        await AddRuleAsync(parentPrincipal, _readPermId, AccessEffect.Deny, ScopeMode.Selected, "V");
        var childA = await CreateGrantedRoleAsync("CHILD_A", AccessEffect.Allow, parent.Id, "F", "V");
        var childB = await CreateGrantedRoleAsync("CHILD_B", AccessEffect.Allow, parent.Id, "F", "V");
        await AssignAsync(childA, childB);

        var result = await ScopesAsync();

        result.ScopeMode.ShouldBe(ScopeMode.Selected);
        result.Keys.ShouldBe(["F"]);
        await AssertInvariantAsync();
    }

    [Test]
    public async Task DenyAllOnPath_ReturnsEmptySelection()
    {
        var (parent, parentPrincipal) = await RoleAsync("PARENT");
        await AddRuleAsync(parentPrincipal, _readPermId, AccessEffect.Deny, ScopeMode.All);
        var child = await CreateGrantedRoleAsync("CHILD", AccessEffect.Allow, parent.Id, "F", "V");
        await AssignAsync(child);

        var result = await ScopesAsync();

        result.ScopeMode.ShouldBe(ScopeMode.Selected);
        result.Keys.ShouldBeEmpty();
        await AssertInvariantAsync();
    }

    [Test]
    public async Task Prerequisite_IntersectsWithTheRequiredPermission()
    {
        await AddRuleAsync(_principalId, _readPermId, AccessEffect.Allow, ScopeMode.Selected, "F");
        await AddRuleAsync(_principalId, _editPermId, AccessEffect.Allow, ScopeMode.Selected, "F", "V");

        var result = await ScopesAsync("Products.Edit");

        result.ScopeMode.ShouldBe(ScopeMode.Selected);
        result.Keys.ShouldBe(["F"]);
        await AssertInvariantAsync("Products.Edit");
    }

    [Test]
    public async Task Delegation_AddsKeysUntilItExpiresOrIsRevoked()
    {
        var (delegatorId, delegatorPrincipal) = await CreateMemberAsync("delegator@test.com");
        await AddRuleAsync(delegatorPrincipal, _readPermId, AccessEffect.Allow, ScopeMode.Selected, "F", "V");
        var delegation = await AddDelegatedRuleAsync(delegatorId, ["F", "V"]);

        var granted = await ScopesAsync();
        granted.ScopeMode.ShouldBe(ScopeMode.Selected);
        granted.Keys.ShouldBe(["F", "V"]);
        await AssertInvariantAsync();

        delegation.SetStatus(AccessRuleStatus.Inactive);
        await Context.SaveChangesAsync(default);

        var revoked = await ScopesAsync();
        revoked.ScopeMode.ShouldBe(ScopeMode.Selected);
        revoked.Keys.ShouldBeEmpty();
        await AssertInvariantAsync();

        await AddDelegatedRuleAsync(delegatorId, ["F", "V"], DateTime.UtcNow.AddMinutes(-5));

        var expired = await ScopesAsync();
        expired.Keys.ShouldBeEmpty();
        await AssertInvariantAsync();
    }

    [Test]
    public async Task InactiveOrExpiredRole_ContributesNothing()
    {
        var inactive = await CreateGrantedRoleAsync("INACTIVE", AccessEffect.Allow, null, "F", "V");
        (await Context.Roles.SingleAsync(r => r.Id == inactive)).SetStatus(RoleStatus.Inactive);
        await Context.SaveChangesAsync(default);
        await AssignAsync(inactive);

        var inactiveResult = await ScopesAsync();
        inactiveResult.ScopeMode.ShouldBe(ScopeMode.Selected);
        inactiveResult.Keys.ShouldBeEmpty();
        await AssertInvariantAsync();

        var expiredRole = new Role(_companyId, _appId, "EXPIRED", "Expired", validUntil: DateTime.UtcNow.AddMinutes(-5));
        Context.Roles.Add(expiredRole);
        await Context.SaveChangesAsync(default);
        var expiredPrincipal = await CreateAuthPrincipalForRoleAsync(expiredRole.Id, _companyId, _appId);
        await AddRuleAsync(expiredPrincipal.Id, _readPermId, AccessEffect.Allow, ScopeMode.Selected, "F", "V");
        await AssignAsync(expiredRole.Id);

        var expiredResult = await ScopesAsync();
        expiredResult.Keys.ShouldBeEmpty();
        await AssertInvariantAsync();
    }

    [Test]
    public async Task CompanyAndGlobalSuperAdmin_ReturnAll()
    {
        var companyAdmin = await CreateRoleAsync(_companyId, _appId, "Company Super", "CSA", RoleKind.CompanySuperAdmin);
        await CreateAuthPrincipalForRoleAsync(companyAdmin.Id, _companyId, _appId);
        await AssignAsync(companyAdmin.Id);

        var companyResult = await ScopesAsync();
        companyResult.ScopeMode.ShouldBe(ScopeMode.All);
        await AssertInvariantAsync();

        var membership = await Context.UserCompanies.Include(uc => uc.Roles).SingleAsync(uc => uc.UserId == _userId);
        membership.RemoveRole(companyAdmin.Id);
        var globalAdmin = await CreateRoleAsync(_companyId, _appId, "Global Super", "GSA", RoleKind.GlobalSuperAdmin);
        await CreateAuthPrincipalForRoleAsync(globalAdmin.Id, _companyId, _appId);
        membership.AddRole(globalAdmin.Id);
        await Context.SaveChangesAsync(default);

        var globalResult = await ScopesAsync();
        globalResult.ScopeMode.ShouldBe(ScopeMode.All);
        await AssertInvariantAsync();
    }

    [Test]
    public async Task RevisionChange_RecomputesInsteadOfServingThePreviousSet()
    {
        var rule = await AddRuleAsync(_principalId, _readPermId, AccessEffect.Allow, ScopeMode.Selected, "F");
        var before = await ScopesAsync();
        before.Keys.ShouldBe(["F"]);

        rule.AddScope("Workstation", "V");
        await Context.SaveChangesAsync(default);

        var after = await ScopesAsync();
        after.Keys.ShouldBe(["F", "V"]);
        after.Revision.ShouldNotBe(before.Revision);
        await AssertInvariantAsync();
    }

    private async Task AssertInvariantAsync(string permissionCode = "Products.Read")
    {
        var result = await ScopesAsync(permissionCode);
        foreach (var key in Universe)
        {
            var decision = await EvaluateAsync(new AccessRequest(_userId, _companyId, "QC", permissionCode, "Workstation", key));
            var member = result.ScopeMode == ScopeMode.All || result.Keys.Contains(key);
            decision.Allowed.ShouldBe(member,
                $"{permissionCode} key {key}: evaluate={decision.Allowed} ({decision.ReasonCode}), scopes={result.ScopeMode} [{string.Join(",", result.Keys)}]");
        }
    }

    private async Task<EffectiveScopesResult> ScopesAsync(string permissionCode = "Products.Read")
    {
        using var scope = Factory.Services.CreateScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IAccessEvaluator>();
        return await evaluator.EvaluateEffectiveScopesAsync(
            new EffectiveScopesRequest(_userId, _companyId, "QC", permissionCode, "Workstation"));
    }

    private async Task<(Guid UserId, Guid PrincipalId)> CreateMemberAsync(string email)
    {
        var user = await CreateUserAsync(email, "Test123!");
        var membership = await CreateUserCompanyAsync(user.Id, _companyId, Guid.NewGuid());
        var principal = await CreateAuthPrincipalForUserCompanyAsync(membership.Id, _companyId, _appId);
        ((ApplicationDbContext)Context).Entry(membership).Property("PrincipalId").CurrentValue = principal.Id;
        await Context.SaveChangesAsync(default);
        return (user.Id, principal.Id);
    }

    private async Task<(Role Role, Guid PrincipalId)> RoleAsync(string code, Guid? parentRoleId = null, RoleKind kind = RoleKind.Standard)
    {
        var role = await CreateRoleAsync(_companyId, _appId, code, code, kind, parentRoleId);
        var principal = await CreateAuthPrincipalForRoleAsync(role.Id, _companyId, _appId);
        return (role, principal.Id);
    }

    private async Task<Guid> CreateGrantedRoleAsync(string code, AccessEffect effect, Guid? parentRoleId, params string[] keys)
    {
        var (_, principalId) = await RoleAsync(code, parentRoleId);
        await AddRuleAsync(principalId, _readPermId, effect, keys.Length == 0 ? ScopeMode.None : ScopeMode.Selected, keys);
        return (await Context.Roles.SingleAsync(r => r.Code == code && r.CompanyId == _companyId)).Id;
    }

    private async Task AssignAsync(params Guid[] roleIds)
    {
        var membership = await Context.UserCompanies.Include(uc => uc.Roles).SingleAsync(uc => uc.UserId == _userId && uc.CompanyId == _companyId);
        foreach (var roleId in roleIds)
            membership.AddRole(roleId);
        await Context.SaveChangesAsync(default);
    }

    private async Task<AccessRule> AddRuleAsync(Guid principalId, Guid permissionId, AccessEffect effect, ScopeMode mode, params string[] keys)
    {
        var rule = new AccessRule(principalId, permissionId, effect, AccessRuleOrigin.Manual, mode);
        if (mode == ScopeMode.Selected)
        {
            foreach (var key in keys)
                rule.AddScope("Workstation", key);
        }

        Context.AccessRules.Add(rule);
        await Context.SaveChangesAsync(default);
        return rule;
    }

    private async Task<AccessRule> AddDelegatedRuleAsync(Guid delegatorUserId, string[] keys, DateTime? validUntil = null)
    {
        var rule = new AccessRule(_principalId, _readPermId, AccessEffect.Allow, AccessRuleOrigin.Delegated, ScopeMode.Selected, validUntil: validUntil, delegatedFromUserId: delegatorUserId);
        foreach (var key in keys)
            rule.AddScope("Workstation", key);
        Context.AccessRules.Add(rule);
        await Context.SaveChangesAsync(default);
        return rule;
    }
}

[TestFixture]
public class EffectiveScopesCacheTests : TestBase
{
    private FakeTimeProvider _clock = null!;
    private Guid _companyId;
    private Guid _readPermId;
    private Guid _userId;
    private Guid _principalId;

    protected override SqliteTestWebApplicationFactory CreateFactory()
    {
        _clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        return new SqliteTestWebApplicationFactory(_clock);
    }

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _, _readPermId, _, _) = await SeedTestDataAsync();
        var user = await CreateUserAsync("cached-scopes@test.com", "Test123!");
        _userId = user.Id;
        var membership = await CreateUserCompanyAsync(_userId, _companyId, Guid.NewGuid());
        var principal = await CreateAuthPrincipalForUserCompanyAsync(membership.Id, _companyId, await Context.Applications.Where(a => a.Code == "QC").Select(a => a.Id).SingleAsync());
        _principalId = principal.Id;
        ((ApplicationDbContext)Context).Entry(membership).Property("PrincipalId").CurrentValue = _principalId;
        await Context.SaveChangesAsync(default);
    }

    [Test]
    public async Task RawSqlMutation_IsIgnoredUntilTheCacheExpires()
    {
        var rule = new AccessRule(_principalId, _readPermId, AccessEffect.Allow, AccessRuleOrigin.Manual, ScopeMode.Selected);
        rule.AddScope("Workstation", "F");
        Context.AccessRules.Add(rule);
        await Context.SaveChangesAsync(default);

        (await ScopesAsync()).Keys.ShouldBe(["F"]);

        await ((ApplicationDbContext)Context).Database.ExecuteSqlRawAsync("DELETE FROM \"AccessRules\" WHERE \"Id\" = {0}", rule.Id);

        (await ScopesAsync()).Keys.ShouldBe(["F"]);

        _clock.Advance(AccessEvaluator.MaxCacheLifetime + TimeSpan.FromSeconds(1));

        (await ScopesAsync()).Keys.ShouldBeEmpty();
    }

    private async Task<EffectiveScopesResult> ScopesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IAccessEvaluator>();
        return await evaluator.EvaluateEffectiveScopesAsync(
            new EffectiveScopesRequest(_userId, _companyId, "QC", "Products.Read", "Workstation"));
    }
}
