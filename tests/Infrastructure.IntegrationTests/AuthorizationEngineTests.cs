using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Organization;
using CompanyAccessManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.IntegrationTests;

[TestFixture]
public class AuthorizationEngineTests : TestBase
{
    private Guid _companyId;
    private Guid _appId;
    private Guid _productsReadPermId;
    private Guid _productsEditPermId;
    private Guid _testUserId;
    private Guid _productsDeletePermId;
    private Guid _testPrincipalId;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _appId, _productsReadPermId, _productsEditPermId, _productsDeletePermId) = await SeedTestDataAsync();

        // Create test user
        var user = await CreateUserAsync("test@test.com", "Test123!");
        _testUserId = user.Id;

        // Create user company with temporary principalId
        var userCompany = await CreateUserCompanyAsync(_testUserId, _companyId, Guid.NewGuid());

        // Create AuthPrincipal for the UserCompany - this generates the actual principal Id
        var authPrincipal = await CreateAuthPrincipalForUserCompanyAsync(userCompany.Id, _companyId, _appId);
        _testPrincipalId = authPrincipal.Id;

        // Update UserCompany's PrincipalId to match the AuthPrincipal using EF Core change tracking
        if (Context is ApplicationDbContext dbContext)
        {
            dbContext.Entry(userCompany).Property("PrincipalId").CurrentValue = _testPrincipalId;
            await dbContext.SaveChangesAsync(default);
        }
    }

    [Test]
    public async Task EvaluateAsync_NoMembership_ReturnsDenied()
    {
        var otherCompanyId = Guid.NewGuid();
        var request = new AccessRequest(_testUserId, otherCompanyId, "QC", "Products.Read");

        var decision = await EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_MEMBERSHIP"));
    }

    [Test]
    public async Task EvaluateAsync_DirectUserPermission_ReturnsAllowed()
    {
        // Grant direct permission
        await CreateAccessRuleAsync(_testPrincipalId, _productsReadPermId, AccessEffect.Allow);

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read");
        
        var decision = await EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.True);
        Assert.That(decision.ReasonCode, Is.EqualTo("ALLOWED"));
    }

    [Test]
    public async Task EvaluateAsync_RolePermission_ReturnsAllowed()
    {
        // Create role and assign to user
        var role = await CreateRoleAsync(_companyId, _appId, "Reader", "READER");
        var authPrincipal = await CreateAuthPrincipalForRoleAsync(role.Id, _companyId, _appId);
        await CreateAccessRuleAsync(authPrincipal.Id, _productsReadPermId, AccessEffect.Allow);

        var userCompany = await Context.UserCompanies.FirstOrDefaultAsync(uc => uc.UserId == _testUserId);
        userCompany!.AddRole(role.Id);
        await Context.SaveChangesAsync(default);

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read");
        var decision = await EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.True);
    }

    [Test]
    public async Task EvaluateAsync_DenyBlocksAllow()
    {
        // Grant allow via role
        var role = await CreateRoleAsync(_companyId, _appId, "Editor", "EDITOR");
        var authPrincipal = await CreateAuthPrincipalForRoleAsync(role.Id, _companyId, _appId);
        await CreateAccessRuleAsync(authPrincipal.Id, _productsEditPermId, AccessEffect.Allow);

        var userCompany = await Context.UserCompanies.FirstOrDefaultAsync(uc => uc.UserId == _testUserId);
        userCompany!.AddRole(role.Id);
        await Context.SaveChangesAsync(default);

        // Add deny on same role for same permission
        await CreateAccessRuleAsync(authPrincipal.Id, _productsEditPermId, AccessEffect.Deny);

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Edit");
        var decision = await EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_EXPLICIT_DENY"));
    }

    [Test]
    public async Task EvaluateAsync_ScopeMismatch_ReturnsDenied()
    {
        // Grant permission with scope Workshop:A
        await CreateAccessRuleAsync(_testPrincipalId, _productsReadPermId, AccessEffect.Allow, ScopeMode.Selected, "Workshop", "A");

        // Request with scope Workshop:B
        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read", "Workshop", "B");
        var decision = await EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_NO_PERMISSION"));
    }

    [Test]
    public async Task EvaluateAsync_ScopeAll_GrantsAllScopes()
    {
        // Grant permission with ScopeMode.All
        await CreateAccessRuleAsync(_testPrincipalId, _productsReadPermId, AccessEffect.Allow, ScopeMode.All);

        // Request with any scope
        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read", "Workshop", "Any");
        var decision = await EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.True);
    }

    [Test]
    public async Task EvaluateAsync_NoneScope_RequiresNoScope()
    {
        // Grant permission with ScopeMode.None
        await CreateAccessRuleAsync(_testPrincipalId, _productsReadPermId, AccessEffect.Allow, ScopeMode.None);

        // Request without scope - should work
        var requestNoScope = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read");
        var decisionNoScope = await EvaluateAsync(requestNoScope);
        Assert.That(decisionNoScope.Allowed, Is.True);

        // Request with scope - should fail
        var requestWithScope = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read", "Workshop", "A");
        var decisionWithScope = await EvaluateAsync(requestWithScope);
        Assert.That(decisionWithScope.Allowed, Is.False);
    }

    [Test]
    public async Task EvaluateAsync_MultipleRoles_UnionsPermissions()
    {
        // Role 1: Products.Read
        var role1 = await CreateRoleAsync(_companyId, _appId, "Reader", "READER");
        var authPrincipal1 = await CreateAuthPrincipalForRoleAsync(role1.Id, _companyId, _appId);
        await CreateAccessRuleAsync(authPrincipal1.Id, _productsReadPermId, AccessEffect.Allow);

        // Role 2: Products.Edit
        var role2 = await CreateRoleAsync(_companyId, _appId, "Editor", "EDITOR");
        var authPrincipal2 = await CreateAuthPrincipalForRoleAsync(role2.Id, _companyId, _appId);
        await CreateAccessRuleAsync(authPrincipal2.Id, _productsEditPermId, AccessEffect.Allow);

        var userCompany = await Context.UserCompanies.FirstOrDefaultAsync(uc => uc.UserId == _testUserId);
        userCompany!.AddRole(role1.Id);
        userCompany.AddRole(role2.Id);
        await Context.SaveChangesAsync(default);

        // Should have both permissions
        var readRequest = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read");
        var readDecision = await EvaluateAsync(readRequest);
        Assert.That(readDecision.Allowed, Is.True);

        var editRequest = new AccessRequest(_testUserId, _companyId, "QC", "Products.Edit");
        var editDecision = await EvaluateAsync(editRequest);
        Assert.That(editDecision.Allowed, Is.True);
    }

    [Test]
    public async Task EvaluateAsync_CompanySuperAdmin_GrantsAll()
    {
        var role = await CreateRoleAsync(_companyId, _appId, "SuperAdmin", "SUPERADMIN", RoleKind.CompanySuperAdmin);
        await CreateAuthPrincipalForRoleAsync(role.Id, _companyId, _appId);

        var userCompany = await Context.UserCompanies.FirstOrDefaultAsync(uc => uc.UserId == _testUserId);
        userCompany!.AddRole(role.Id);
        await Context.SaveChangesAsync(default);

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Delete");
        var decision = await EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.True);
        Assert.That(decision.ReasonCode, Is.EqualTo("ALLOWED_COMPANY_SUPER_ADMIN"));
    }

    [Test]
    public async Task EvaluateAsync_PrerequisiteGate_EditRequiresRead()
    {
        // Grant Edit but NOT Read
        await CreateAccessRuleAsync(_testPrincipalId, _productsEditPermId, AccessEffect.Allow);

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Edit");
        var decision = await EvaluateAsync(request);

        // Should be denied because Edit requires Read and Read is denied
        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_PREREQUISITE_GATE"));
    }

    [Test]
    public async Task EvaluateAsync_PrerequisiteGate_EditWithReadAllowed()
    {
        // Grant both Edit and Read
        await CreateAccessRuleAsync(_testPrincipalId, _productsReadPermId, AccessEffect.Allow);
        await CreateAccessRuleAsync(_testPrincipalId, _productsEditPermId, AccessEffect.Allow);

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Edit");
        var decision = await EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.True);
    }

    // ============================================================================
    // Additional Comprehensive Authorization Tests
    // ============================================================================

    [Test]
    public async Task EvaluateAsync_ScopeMismatchDeny_DoesNotBlock()
    {
        // Role A: DENY Products.Edit @ Workshop A
        // Role B: ALLOW Products.Edit @ Workshop B
        // Request: Products.Edit @ Workshop B; the DENY does not match the requested scope

        var roleA = await CreateRoleAsync(_companyId, _appId, "RoleA", "ROLE_A");
        var authPrincipalA = await CreateAuthPrincipalForRoleAsync(roleA.Id, _companyId, _appId);

        var roleB = await CreateRoleAsync(_companyId, _appId, "RoleB", "ROLE_B");
        var authPrincipalB = await CreateAuthPrincipalForRoleAsync(roleB.Id, _companyId, _appId);

        // DENY on Role A for Workshop A
        await CreateAccessRuleAsync(authPrincipalA.Id, _productsEditPermId, AccessEffect.Deny, ScopeMode.Selected, "Workshop", "A");

        // ALLOW on Role B for Workshop B
        // Also grant Read (prerequisite for Edit)
        await CreateAccessRuleAsync(authPrincipalB.Id, _productsReadPermId, AccessEffect.Allow, ScopeMode.Selected, "Workshop", "B");
        await CreateAccessRuleAsync(authPrincipalB.Id, _productsEditPermId, AccessEffect.Allow, ScopeMode.Selected, "Workshop", "B");

        var userCompany = await Context.UserCompanies.FirstOrDefaultAsync(uc => uc.UserId == _testUserId);
        userCompany!.AddRole(roleA.Id);
        userCompany.AddRole(roleB.Id);
        await Context.SaveChangesAsync(default);

        // Request for Workshop B - DENY on Workshop A should not block Workshop B
        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Edit", "Workshop", "B");
        var decision = await EvaluateAsync(request);

        // Expected: ALLOWED because DENY is scope-matched to Workshop A only
        Assert.That(decision.Allowed, Is.True);
        Assert.That(decision.ReasonCode, Is.EqualTo("ALLOWED"));
    }

    [Test]
    public async Task EvaluateAsync_SelfDeny_BlocksAllow()
    {
        // User has a role with ALLOW
        // Same role also has DENY for same permission
        // Expected: DENIED (self DENY blocks)

        var role = await CreateRoleAsync(_companyId, _appId, "Editor", "EDITOR");
        var authPrincipal = await CreateAuthPrincipalForRoleAsync(role.Id, _companyId, _appId);

        // Grant ALLOW
        await CreateAccessRuleAsync(authPrincipal.Id, _productsEditPermId, AccessEffect.Allow);

        // Add DENY on same role
        await CreateAccessRuleAsync(authPrincipal.Id, _productsEditPermId, AccessEffect.Deny);

        var userCompany = await Context.UserCompanies.FirstOrDefaultAsync(uc => uc.UserId == _testUserId);
        userCompany!.AddRole(role.Id);
        await Context.SaveChangesAsync(default);

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Edit");
        var decision = await EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_EXPLICIT_DENY"));
    }

    [Test]
    public async Task EvaluateAsync_AncestorDeny_BlocksDescendantAllow()
    {
        // Role hierarchy: Parent -> Child
        // Parent has DENY Products.Edit
        // Child has ALLOW Products.Edit
        // User assigned to Child
        // Expected: DENIED (ancestor DENY blocks descendant)

        var parentRole = await CreateRoleAsync(_companyId, _appId, "Parent", "PARENT");
        var parentAuthPrincipal = await CreateAuthPrincipalForRoleAsync(parentRole.Id, _companyId, _appId);

        var childRole = await CreateRoleAsync(_companyId, _appId, "Child", "CHILD", RoleKind.Standard, parentRole.Id);
        var childAuthPrincipal = await CreateAuthPrincipalForRoleAsync(childRole.Id, _companyId, _appId);

        // Parent DENY
        await CreateAccessRuleAsync(parentAuthPrincipal.Id, _productsEditPermId, AccessEffect.Deny);

        // Child ALLOW
        await CreateAccessRuleAsync(childAuthPrincipal.Id, _productsEditPermId, AccessEffect.Allow);

        var userCompany = await Context.UserCompanies.FirstOrDefaultAsync(uc => uc.UserId == _testUserId);
        userCompany!.AddRole(childRole.Id);
        await Context.SaveChangesAsync(default);

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Edit");
        var decision = await EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_EXPLICIT_DENY"));
    }

    [Test]
    public async Task EvaluateAsync_FutureDescendantUnderDeny_Blocked()
    {
        // Role A has DENY
        // Role B is created later as child of Role A
        // User assigned to Role B
        // Expected: DENIED (future descendants inherit DENY boundary)

        var roleA = await CreateRoleAsync(_companyId, _appId, "RoleA", "ROLE_A");
        var authPrincipalA = await CreateAuthPrincipalForRoleAsync(roleA.Id, _companyId, _appId);

        // Role A DENY
        await CreateAccessRuleAsync(authPrincipalA.Id, _productsEditPermId, AccessEffect.Deny);

        // Create Role B as child of Role A (after DENY exists)
        var roleB = await CreateRoleAsync(_companyId, _appId, "RoleB", "ROLE_B", RoleKind.Standard, roleA.Id);
        var authPrincipalB = await CreateAuthPrincipalForRoleAsync(roleB.Id, _companyId, _appId);

        // Role B ALLOW
        await CreateAccessRuleAsync(authPrincipalB.Id, _productsEditPermId, AccessEffect.Allow);

        var userCompany = await Context.UserCompanies.FirstOrDefaultAsync(uc => uc.UserId == _testUserId);
        userCompany!.AddRole(roleB.Id);
        await Context.SaveChangesAsync(default);

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Edit");
        var decision = await EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_EXPLICIT_DENY"));
    }

    [Test]
    public async Task EvaluateAsync_TransitivePrerequisite_BlocksWhenAncestorDenied()
    {
        // Edit -> requires Read -> requires View
        // Parent DENYs View; Child ALLOWs Edit, Read and View; user holds Child
        // The ancestor DENY of a transitive prerequisite closes the gate for Edit

        var productsViewPerm = await CreatePermissionAsync(
            (await Context.Resources.FirstAsync(r => r.Code == "Products")).Id,
            "View");

        await CreatePermissionImplicationAsync(_productsEditPermId, _productsReadPermId);
        await CreatePermissionImplicationAsync(_productsReadPermId, productsViewPerm);

        var parent = await CreateRoleAsync(_companyId, _appId, "Parent", "PARENT");
        var parentPrincipal = await CreateAuthPrincipalForRoleAsync(parent.Id, _companyId, _appId);
        var child = await CreateRoleAsync(_companyId, _appId, "Child", "CHILD", RoleKind.Standard, parent.Id);
        var childPrincipal = await CreateAuthPrincipalForRoleAsync(child.Id, _companyId, _appId);

        await CreateAccessRuleAsync(parentPrincipal.Id, productsViewPerm, AccessEffect.Deny);
        await CreateAccessRuleAsync(childPrincipal.Id, _productsEditPermId, AccessEffect.Allow);
        await CreateAccessRuleAsync(childPrincipal.Id, _productsReadPermId, AccessEffect.Allow);
        await CreateAccessRuleAsync(childPrincipal.Id, productsViewPerm, AccessEffect.Allow);

        await AssignRolesToTestUserAsync(child.Id);

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Edit");
        var decision = await EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_PREREQUISITE_GATE"));
    }

    [Test]
    public async Task EvaluateAsync_DelegationValid_AllowsAccess()
    {
        // Delegator has permission
        // Delegatee receives delegation
        // Delegation is valid (not expired, source still has permission)
        // Expected: ALLOWED

        var delegatorUser = await CreateUserAsync("delegator@test.com", "Test123!");
        var delegatorUserCompany = await CreateUserCompanyAsync(delegatorUser.Id, _companyId, Guid.NewGuid());
        var delegatorAuthPrincipal = await CreateAuthPrincipalForUserCompanyAsync(delegatorUserCompany.Id, _companyId, _appId);
        var delegatorPrincipalId = delegatorAuthPrincipal.Id;

        // Update delegator UserCompany's PrincipalId to match
        typeof(UserCompany).GetProperty("PrincipalId")!.SetValue(delegatorUserCompany, delegatorPrincipalId);
        await Context.SaveChangesAsync(default);

        // Delegator has ALLOW
        await CreateAccessRuleAsync(delegatorPrincipalId, _productsReadPermId, AccessEffect.Allow);

        // Create delegation to test user
        var delegationRule = new AccessRule(_testPrincipalId, _productsReadPermId, AccessEffect.Allow, AccessRuleOrigin.Delegated, ScopeMode.None, validUntil: DateTime.UtcNow.AddHours(1), delegatedFromUserId: delegatorUser.Id);
        Context.AccessRules.Add(delegationRule);
        await Context.SaveChangesAsync(default);

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read");
        var decision = await EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.True);
    }

    [Test]
    public async Task EvaluateAsync_DelegationExpired_DeniesAccess()
    {
        // Delegation has expired
        // Expected: DENIED

        var delegatorUser = await CreateUserAsync("delegator@test.com", "Test123!");
        var delegatorUserCompany = await CreateUserCompanyAsync(delegatorUser.Id, _companyId, Guid.NewGuid());
        var delegatorAuthPrincipal = await CreateAuthPrincipalForUserCompanyAsync(delegatorUserCompany.Id, _companyId, _appId);
        var delegatorPrincipalId = delegatorAuthPrincipal.Id;

        // Update delegator UserCompany's PrincipalId to match using EF Core change tracking
        if (Context is ApplicationDbContext dbContext)
        {
            dbContext.Entry(delegatorUserCompany).Property("PrincipalId").CurrentValue = delegatorPrincipalId;
            await dbContext.SaveChangesAsync(default);
        }

        // Delegator has ALLOW
        await CreateAccessRuleAsync(delegatorPrincipalId, _productsReadPermId, AccessEffect.Allow);

        // Create EXPIRED delegation to test user
        var delegationRule = new AccessRule(_testPrincipalId, _productsReadPermId, AccessEffect.Allow, AccessRuleOrigin.Delegated, ScopeMode.None, validUntil: DateTime.UtcNow.AddHours(-1));
        Context.AccessRules.Add(delegationRule);
        await Context.SaveChangesAsync(default);

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read");
        var decision = await EvaluateAsync(request);

        // Expired delegation should be denied
        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_NO_PERMISSION"));
    }

    [Test]
    public async Task EvaluateAsync_DelegationSourceLost_DeniesAccess()
    {
        // Delegator loses permission
        // Delegation should become invalid
        // Expected: DENIED

        var delegatorUser = await CreateUserAsync("delegator@test.com", "Test123!");
        var delegatorUserCompany = await CreateUserCompanyAsync(delegatorUser.Id, _companyId, Guid.NewGuid());
        var delegatorAuthPrincipal = await CreateAuthPrincipalForUserCompanyAsync(delegatorUserCompany.Id, _companyId, _appId);
        var delegatorPrincipalId = delegatorAuthPrincipal.Id;

        // Update delegator UserCompany's PrincipalId to match using EF Core change tracking
        if (Context is ApplicationDbContext dbContext)
        {
            dbContext.Entry(delegatorUserCompany).Property("PrincipalId").CurrentValue = delegatorPrincipalId;
            await dbContext.SaveChangesAsync(default);
        }

        // Create delegation to test user (delegator has permission initially)
        var delegationRule = new AccessRule(_testPrincipalId, _productsReadPermId, AccessEffect.Allow, AccessRuleOrigin.Delegated, ScopeMode.None, validUntil: DateTime.UtcNow.AddHours(1), delegatedFromUserId: delegatorUser.Id);
        Context.AccessRules.Add(delegationRule);

        // Delegator has ALLOW initially
        await CreateAccessRuleAsync(delegatorPrincipalId, _productsReadPermId, AccessEffect.Allow);
        await Context.SaveChangesAsync(default);

        // Now REMOVE delegator's permission
        var delegatorRule = await Context.AccessRules.FirstOrDefaultAsync(r => r.PrincipalId == delegatorPrincipalId && r.PermissionId == _productsReadPermId);
        if (delegatorRule != null)
        {
            Context.AccessRules.Remove(delegatorRule);
            await Context.SaveChangesAsync(default);
        }

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read");
        var decision = await EvaluateAsync(request);

        // Delegator lost permission, delegation should be denied
        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_NO_PERMISSION"));
    }

    [Test]
    public async Task EvaluateAsync_CompanySuperAdmin_CrossCompanyDenied()
    {
        // Company Super Admin in Company A
        // Try to access Company B
        // Expected: DENIED (company isolation)

        var companyB = await CreateCompanyAsync("COMPB", "Company B");

        var superAdminRole = await CreateRoleAsync(_companyId, _appId, "SuperAdmin", "SUPERADMIN", RoleKind.CompanySuperAdmin);
        await CreateAuthPrincipalForRoleAsync(superAdminRole.Id, _companyId, _appId);

        var userCompany = await Context.UserCompanies.FirstOrDefaultAsync(uc => uc.UserId == _testUserId);
        userCompany!.AddRole(superAdminRole.Id);
        await Context.SaveChangesAsync(default);

        // Try to access Company B
        var request = new AccessRequest(_testUserId, companyB, "QC", "Products.Read");
        var decision = await EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_MEMBERSHIP"));
    }

    [Test]
    public async Task EvaluateAsync_GlobalSuperAdmin_CrossCompanyAllowed()
    {
        // Global Super Admin should have access across companies
        var companyB = await CreateCompanyAsync("COMPB", "Company B");

        var globalSuperAdminRole = await CreateRoleAsync(_companyId, _appId, "GlobalSuperAdmin", "GLOBAL_SUPER", RoleKind.GlobalSuperAdmin);
        await CreateAuthPrincipalForRoleAsync(globalSuperAdminRole.Id, _companyId, _appId);

        var userCompany = await Context.UserCompanies.FirstOrDefaultAsync(uc => uc.UserId == _testUserId);
        userCompany!.AddRole(globalSuperAdminRole.Id);
        await Context.SaveChangesAsync(default);

        // Try to access Company B
        var request = new AccessRequest(_testUserId, companyB, "QC", "Products.Read");
        var decision = await EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.True);
        Assert.That(decision.ReasonCode, Is.EqualTo("ALLOWED_GLOBAL_SUPER_ADMIN"));
    }

    [Test]
    public async Task EvaluateAsync_ScopeNone_RequiresNoScopeInRequest()
    {
        // Rule has ScopeMode.None
        // Request without scope -> ALLOWED
        // Request with scope -> DENIED

        await CreateAccessRuleAsync(_testPrincipalId, _productsReadPermId, AccessEffect.Allow, ScopeMode.None);

        // Request without scope
        var requestNoScope = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read");
        var decisionNoScope = await EvaluateAsync(requestNoScope);
        Assert.That(decisionNoScope.Allowed, Is.True);

        // Request with scope
        var requestWithScope = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read", "Workshop", "A");
        var decisionWithScope = await EvaluateAsync(requestWithScope);
        Assert.That(decisionWithScope.Allowed, Is.False);
    }

    [Test]
    public async Task EvaluateAsync_ScopeAll_AllowsAnyScope()
    {
        // Rule has ScopeMode.All
        // Request with any scope -> ALLOWED

        await CreateAccessRuleAsync(_testPrincipalId, _productsReadPermId, AccessEffect.Allow, ScopeMode.All);

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read", "Workshop", "AnyScope");
        var decision = await EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.True);
    }

    [Test]
    public async Task EvaluateAsync_ApplicationIsolation_NoCrossAppAccess()
    {
        // User has permission in App A
        // Request for App B (which has no Products.Read permission)
        // Expected: DENIED

        var appB = await CreateApplicationAsync("APPB", "App B");
        // Note: No Products resource created in App B

        // Grant permission in App A (already seeded)
        await CreateAccessRuleAsync(_testPrincipalId, _productsReadPermId, AccessEffect.Allow);

        // Request for App B
        var request = new AccessRequest(_testUserId, _companyId, "APPB", "Products.Read");
        var decision = await EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_PERMISSION_NOT_FOUND"));
    }

    [Test]
    public async Task EvaluateAsync_MultipleDirectUserRules_UnionPermissions()
    {
        // User has multiple direct ALLOW rules
        // Should get union of all

        await CreateAccessRuleAsync(_testPrincipalId, _productsReadPermId, AccessEffect.Allow);
        await CreateAccessRuleAsync(_testPrincipalId, _productsEditPermId, AccessEffect.Allow);

        var readRequest = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read");
        var readDecision = await EvaluateAsync(readRequest);
        Assert.That(readDecision.Allowed, Is.True);

        var editRequest = new AccessRequest(_testUserId, _companyId, "QC", "Products.Edit");
        var editDecision = await EvaluateAsync(editRequest);
        Assert.That(editDecision.Allowed, Is.True);
    }

    [Test]
    public async Task EvaluateAsync_RoleUp_DescendantAllowEffectiveForAncestorHolder()
    {
        // Grandparent -> Parent -> Child; Child ALLOWs Read; user holds Grandparent
        var (grandparent, _, child) = await CreateRoleChainAsync();
        await CreateAccessRuleAsync(child.PrincipalId, _productsReadPermId, AccessEffect.Allow);
        await AssignRolesToTestUserAsync(grandparent.RoleId);

        var decision = await EvaluateAsync(new AccessRequest(_testUserId, _companyId, "QC", "Products.Read"));

        Assert.That(decision.Allowed, Is.True);
        Assert.That(decision.ReasonCode, Is.EqualTo("ALLOWED"));
    }

    [Test]
    public async Task EvaluateAsync_RoleDown_AncestorAllowNotInheritedByDescendantHolder()
    {
        // Grandparent -> Parent -> Child; Grandparent ALLOWs Read; user holds Child
        var (grandparent, _, child) = await CreateRoleChainAsync();
        await CreateAccessRuleAsync(grandparent.PrincipalId, _productsReadPermId, AccessEffect.Allow);
        await AssignRolesToTestUserAsync(child.RoleId);

        var decision = await EvaluateAsync(new AccessRequest(_testUserId, _companyId, "QC", "Products.Read"));

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_NO_PERMISSION"));
    }

    [Test]
    public async Task EvaluateAsync_RoleUp_DenyBetweenOriginAndAssignedRole_Blocks()
    {
        // Grandparent -> Parent -> Child; Child ALLOWs Read, Parent DENYs Read; user holds Grandparent
        var (grandparent, parent, child) = await CreateRoleChainAsync();
        await CreateAccessRuleAsync(child.PrincipalId, _productsReadPermId, AccessEffect.Allow);
        await CreateAccessRuleAsync(parent.PrincipalId, _productsReadPermId, AccessEffect.Deny);
        await AssignRolesToTestUserAsync(grandparent.RoleId);

        var decision = await EvaluateAsync(new AccessRequest(_testUserId, _companyId, "QC", "Products.Read"));

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_EXPLICIT_DENY"));
    }

    [Test]
    public async Task EvaluateAsync_DirectAllowAndDirectDeny_Denied()
    {
        await CreateAccessRuleAsync(_testPrincipalId, _productsReadPermId, AccessEffect.Allow);
        await CreateAccessRuleAsync(_testPrincipalId, _productsReadPermId, AccessEffect.Deny);

        var decision = await EvaluateAsync(new AccessRequest(_testUserId, _companyId, "QC", "Products.Read"));

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_EXPLICIT_DENY"));
    }

    [Test]
    public async Task EvaluateAsync_RoleAllowAndDirectDeny_Denied()
    {
        var role = await CreateRoleWithPrincipalAsync("Reader", "READER");
        await CreateAccessRuleAsync(role.PrincipalId, _productsReadPermId, AccessEffect.Allow);
        await CreateAccessRuleAsync(_testPrincipalId, _productsReadPermId, AccessEffect.Deny);
        await AssignRolesToTestUserAsync(role.RoleId);

        var decision = await EvaluateAsync(new AccessRequest(_testUserId, _companyId, "QC", "Products.Read"));

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_EXPLICIT_DENY"));
    }

    [Test]
    public async Task EvaluateAsync_DirectDenyOfPrerequisite_DeniesGlobally()
    {
        var role = await CreateRoleWithPrincipalAsync("Editor", "EDITOR");
        await CreateAccessRuleAsync(role.PrincipalId, _productsReadPermId, AccessEffect.Allow);
        await CreateAccessRuleAsync(role.PrincipalId, _productsEditPermId, AccessEffect.Allow);
        await CreateAccessRuleAsync(_testPrincipalId, _productsReadPermId, AccessEffect.Deny);
        await AssignRolesToTestUserAsync(role.RoleId);

        var decision = await EvaluateAsync(new AccessRequest(_testUserId, _companyId, "QC", "Products.Edit"));

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_EXPLICIT_DENY"));
    }

    [Test]
    public async Task EvaluateAsync_SiblingBranchDeny_SameScope_DoesNotBlockOtherBranch()
    {
        // Two independent roles; A DENYs Read, B ALLOWs Read, both unscoped
        var roleA = await CreateRoleWithPrincipalAsync("RoleA", "ROLE_A");
        var roleB = await CreateRoleWithPrincipalAsync("RoleB", "ROLE_B");
        await CreateAccessRuleAsync(roleA.PrincipalId, _productsReadPermId, AccessEffect.Deny);
        await CreateAccessRuleAsync(roleB.PrincipalId, _productsReadPermId, AccessEffect.Allow);
        await AssignRolesToTestUserAsync(roleA.RoleId, roleB.RoleId);

        var decision = await EvaluateAsync(new AccessRequest(_testUserId, _companyId, "QC", "Products.Read"));

        Assert.That(decision.Allowed, Is.True);
        Assert.That(decision.ReasonCode, Is.EqualTo("ALLOWED"));
        Assert.That(decision.Sources.Select(s => s.SourceId), Is.EquivalentTo(new[] { roleB.RoleId.ToString() }));
    }

    [Test]
    public async Task EvaluateAsync_SharedAncestorDeny_BlocksAllSiblingBranches()
    {
        // Parent DENYs Read; siblings A and B under Parent both ALLOW Read; user holds A and B
        var parent = await CreateRoleWithPrincipalAsync("Parent", "PARENT");
        var roleA = await CreateRoleWithPrincipalAsync("RoleA", "ROLE_A", parent.RoleId);
        var roleB = await CreateRoleWithPrincipalAsync("RoleB", "ROLE_B", parent.RoleId);
        await CreateAccessRuleAsync(parent.PrincipalId, _productsReadPermId, AccessEffect.Deny);
        await CreateAccessRuleAsync(roleA.PrincipalId, _productsReadPermId, AccessEffect.Allow);
        await CreateAccessRuleAsync(roleB.PrincipalId, _productsReadPermId, AccessEffect.Allow);
        await AssignRolesToTestUserAsync(roleA.RoleId, roleB.RoleId);

        var decision = await EvaluateAsync(new AccessRequest(_testUserId, _companyId, "QC", "Products.Read"));

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_EXPLICIT_DENY"));
    }

    [Test]
    public async Task EvaluateAsync_PrerequisiteDeniedOnSamePath_ReturnsPrerequisiteGate()
    {
        var role = await CreateRoleWithPrincipalAsync("Editor", "EDITOR");
        await CreateAccessRuleAsync(role.PrincipalId, _productsEditPermId, AccessEffect.Allow);
        await CreateAccessRuleAsync(role.PrincipalId, _productsReadPermId, AccessEffect.Allow);
        await CreateAccessRuleAsync(role.PrincipalId, _productsReadPermId, AccessEffect.Deny);
        await AssignRolesToTestUserAsync(role.RoleId);

        var decision = await EvaluateAsync(new AccessRequest(_testUserId, _companyId, "QC", "Products.Edit"));

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_PREREQUISITE_GATE"));
    }

    [Test]
    public async Task EvaluateAsync_NoAllowAnywhere_ReturnsNoPermission()
    {
        var role = await CreateRoleWithPrincipalAsync("Empty", "EMPTY");
        await AssignRolesToTestUserAsync(role.RoleId);

        var decision = await EvaluateAsync(new AccessRequest(_testUserId, _companyId, "QC", "Products.Read"));

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_NO_PERMISSION"));
    }

    private async Task<(Guid RoleId, Guid PrincipalId)> CreateRoleWithPrincipalAsync(string name, string code, Guid? parentRoleId = null)
    {
        var role = await CreateRoleAsync(_companyId, _appId, name, code, RoleKind.Standard, parentRoleId);
        var principal = await CreateAuthPrincipalForRoleAsync(role.Id, _companyId, _appId);
        return (role.Id, principal.Id);
    }

    private async Task<((Guid RoleId, Guid PrincipalId) Grandparent, (Guid RoleId, Guid PrincipalId) Parent, (Guid RoleId, Guid PrincipalId) Child)> CreateRoleChainAsync()
    {
        var grandparent = await CreateRoleWithPrincipalAsync("Grandparent", "GRANDPARENT");
        var parent = await CreateRoleWithPrincipalAsync("Parent", "PARENT", grandparent.RoleId);
        var child = await CreateRoleWithPrincipalAsync("Child", "CHILD", parent.RoleId);
        return (grandparent, parent, child);
    }

    private async Task AssignRolesToTestUserAsync(params Guid[] roleIds)
    {
        var userCompany = await Context.UserCompanies.SingleAsync(uc => uc.UserId == _testUserId && uc.CompanyId == _companyId);
        foreach (var roleId in roleIds)
            userCompany.AddRole(roleId);
        await Context.SaveChangesAsync(default);
    }
}
