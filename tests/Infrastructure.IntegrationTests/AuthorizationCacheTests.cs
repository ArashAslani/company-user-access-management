using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Infrastructure.Authorization;
using CompanyAccessManagement.Infrastructure.Data;
using CompanyAccessManagement.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CompanyAccessManagement.IntegrationTests;

/// <summary>
/// Revision-keyed decision cache: every mutation goes through EF (and so through the revision interceptor), except where
/// raw SQL is used on purpose to prove a cached decision is served. Time moves only through the fake clock.
/// </summary>
[TestFixture]
public class AuthorizationCacheTests : TestBase
{
    private FakeTimeProvider _clock = null!;
    private Guid _companyId;
    private Guid _appId;
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
        (_companyId, _appId, _readPermId, _, _) = await SeedTestDataAsync();
        (_userId, _principalId) = await CreateMemberAsync("cached@test.com", _companyId);
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private AccessRequest ReadRequest(Guid? companyId = null, string? scopeType = null, string? scopeKey = null)
        => new(_userId, companyId ?? _companyId, "QC", "Products.Read", scopeType, scopeKey);

    [Test]
    public async Task RepeatedEquivalentEvaluation_UsesCache()
    {
        var rule = await CreateAccessRuleAsync(_principalId, _readPermId, AccessEffect.Allow);
        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeTrue();

        // Raw SQL bypasses the revision interceptor: only a cached decision can still allow.
        await ((ApplicationDbContext)Context).Database.ExecuteSqlRawAsync("DELETE FROM \"AccessRules\" WHERE \"Id\" = {0}", rule.Id);

        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeTrue();

        _clock.Advance(AccessEvaluator.MaxCacheLifetime + TimeSpan.FromSeconds(1));
        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeFalse();
    }

    [Test]
    public async Task RolePermissionRevoked_InvalidatesCachedAllow()
    {
        var (roleId, rolePrincipalId) = await CreateRoleWithPrincipalAsync("READER");
        var rule = await CreateAccessRuleAsync(rolePrincipalId, _readPermId, AccessEffect.Allow);
        await AssignRoleAsync(roleId);
        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeTrue();

        Context.AccessRules.Remove(rule);
        await Context.SaveChangesAsync(default);

        var decision = await EvaluateAsync(ReadRequest());
        decision.Allowed.ShouldBeFalse();
        decision.ReasonCode.ShouldBe("DENIED_NO_PERMISSION");
    }

    [Test]
    public async Task UserRoleRemoved_InvalidatesCachedAllow()
    {
        var (roleId, rolePrincipalId) = await CreateRoleWithPrincipalAsync("READER");
        await CreateAccessRuleAsync(rolePrincipalId, _readPermId, AccessEffect.Allow);
        await AssignRoleAsync(roleId);
        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeTrue();

        var membership = await MembershipAsync();
        membership.RemoveRole(roleId);
        await Context.SaveChangesAsync(default);

        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeFalse();
    }

    [Test]
    public async Task MembershipDisabled_InvalidatesCachedAllow()
    {
        await CreateAccessRuleAsync(_principalId, _readPermId, AccessEffect.Allow);
        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeTrue();

        (await MembershipAsync()).SetStatus(UserCompanyStatus.Inactive);
        await Context.SaveChangesAsync(default);

        var decision = await EvaluateAsync(ReadRequest());
        decision.Allowed.ShouldBeFalse();
        decision.ReasonCode.ShouldBe("DENIED_MEMBERSHIP");
    }

    [Test]
    public async Task RoleExpired_CachedAllowDoesNotSurviveExpiry()
    {
        var role = new Role(_companyId, _appId, "TEMP", "Temporary", RoleKind.Standard, validUntil: Now.AddMinutes(2));
        Context.Roles.Add(role);
        await Context.SaveChangesAsync(default);
        var rolePrincipal = await CreateAuthPrincipalForRoleAsync(role.Id, _companyId, _appId);
        await CreateAccessRuleAsync(rolePrincipal.Id, _readPermId, AccessEffect.Allow);
        await AssignRoleAsync(role.Id);
        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeTrue();

        _clock.Advance(TimeSpan.FromMinutes(1));
        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeTrue();

        // Still well inside the maximum lifetime: only the role's own boundary can end the cached ALLOW.
        _clock.Advance(TimeSpan.FromMinutes(2));
        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeFalse();
    }

    [Test]
    public async Task RuleExpired_CachedAllowDoesNotSurviveExpiry()
    {
        Context.AccessRules.Add(new AccessRule(_principalId, _readPermId, AccessEffect.Allow, AccessRuleOrigin.Manual, ScopeMode.None, validUntil: Now.AddMinutes(2)));
        await Context.SaveChangesAsync(default);
        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeTrue();

        _clock.Advance(TimeSpan.FromMinutes(3));

        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeFalse();
    }

    [Test]
    public async Task RuleNotYetValid_CachedDenyDoesNotSurviveStart()
    {
        Context.AccessRules.Add(new AccessRule(_principalId, _readPermId, AccessEffect.Allow, AccessRuleOrigin.Manual, ScopeMode.None, validFrom: Now.AddMinutes(2)));
        await Context.SaveChangesAsync(default);
        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeFalse();

        _clock.Advance(TimeSpan.FromMinutes(3));

        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeTrue();
    }

    [Test]
    public async Task DelegatorRoleRemoved_InvalidatesDelegateeCachedAllow()
    {
        var (roleId, rolePrincipalId) = await CreateRoleWithPrincipalAsync("READER");
        await CreateAccessRuleAsync(rolePrincipalId, _readPermId, AccessEffect.Allow);
        var (delegatorId, _) = await CreateMemberAsync("delegator@test.com", _companyId);
        var delegatorMembership = await Context.UserCompanies.SingleAsync(uc => uc.UserId == delegatorId);
        delegatorMembership.AddRole(roleId);
        Context.AccessRules.Add(new AccessRule(_principalId, _readPermId, AccessEffect.Allow, AccessRuleOrigin.Delegated, ScopeMode.None,
            validUntil: Now.AddDays(1), delegatedFromUserId: delegatorId));
        await Context.SaveChangesAsync(default);
        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeTrue();

        delegatorMembership.RemoveRole(roleId);
        await Context.SaveChangesAsync(default);

        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeFalse();
    }

    [Test]
    public async Task DifferentCompany_DoesNotShareCacheEntry()
    {
        var otherCompanyId = await CreateCompanyAsync("OTHER", "Other Company");
        var membership = await CreateUserCompanyAsync(_userId, otherCompanyId, Guid.NewGuid());
        var principal = await CreateAuthPrincipalForUserCompanyAsync(membership.Id, otherCompanyId, _appId);
        ((ApplicationDbContext)Context).Entry(membership).Property("PrincipalId").CurrentValue = principal.Id;
        await Context.SaveChangesAsync(default);
        await CreateAccessRuleAsync(_principalId, _readPermId, AccessEffect.Allow);

        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeTrue();

        var other = await EvaluateAsync(ReadRequest(otherCompanyId));
        other.Allowed.ShouldBeFalse();
        other.ReasonCode.ShouldBe("DENIED_NO_PERMISSION");
    }

    [Test]
    public async Task DifferentScope_DoesNotShareCacheEntry()
    {
        await CreateAccessRuleAsync(_principalId, _readPermId, AccessEffect.Allow, ScopeMode.Selected, "Line", "L1");

        (await EvaluateAsync(ReadRequest(scopeType: "Line", scopeKey: "L1"))).Allowed.ShouldBeTrue();

        (await EvaluateAsync(ReadRequest(scopeType: "Line", scopeKey: "L2"))).Allowed.ShouldBeFalse();
        (await EvaluateAsync(ReadRequest())).Allowed.ShouldBeFalse();
    }

    private Task<UserCompany> MembershipAsync()
        => Context.UserCompanies.Include(uc => uc.Roles).SingleAsync(uc => uc.UserId == _userId && uc.CompanyId == _companyId);

    private async Task AssignRoleAsync(Guid roleId)
    {
        (await MembershipAsync()).AddRole(roleId);
        await Context.SaveChangesAsync(default);
    }

    private async Task<(Guid RoleId, Guid PrincipalId)> CreateRoleWithPrincipalAsync(string code)
    {
        var role = await CreateRoleAsync(_companyId, _appId, code, code);
        var principal = await CreateAuthPrincipalForRoleAsync(role.Id, _companyId, _appId);
        return (role.Id, principal.Id);
    }

    private async Task<(Guid UserId, Guid PrincipalId)> CreateMemberAsync(string email, Guid companyId)
    {
        var user = await CreateUserAsync(email, "Test123!");
        var membership = await CreateUserCompanyAsync(user.Id, companyId, Guid.NewGuid());
        var principal = await CreateAuthPrincipalForUserCompanyAsync(membership.Id, companyId, _appId);
        ((ApplicationDbContext)Context).Entry(membership).Property("PrincipalId").CurrentValue = principal.Id;
        await Context.SaveChangesAsync(default);
        return (user.Id, principal.Id);
    }
}
