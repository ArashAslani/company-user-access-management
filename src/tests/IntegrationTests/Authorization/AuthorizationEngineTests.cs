using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Domain.AccessControl;
using CleanArchitecture.Infrastructure.Authorization;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CleanArchitecture.IntegrationTests;

[TestFixture]
public class AuthorizationEngineTests : TestBase
{
    private IAccessEvaluator _accessEvaluator = null!;
    private Guid _companyId;
    private Guid _appId;
    private Guid _productsResourceId;
    private Guid _productsReadPermId;
    private Guid _productsEditPermId;
    private Guid _productsDeletePermId;
    private Guid _labResourceId;
    private Guid _labReadPermId;
    private Guid _labApprovePermId;
    private Guid _testUserId;
    private Guid _testUserCompanyId;
    private Guid _testPrincipalId;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        await SeedTestDataAsync();

        // Create test user
        var user = await CreateUserAsync("test@test.com", "Test123!");
        _testUserId = user.Id;

        // Create user company
        var userCompany = await CreateUserCompanyAsync(_testUserId, _companyId, Guid.NewGuid());
        _testUserCompanyId = userCompany.Id;
        _testPrincipalId = userCompany.PrincipalId;

        // Create access evaluator
        var scope = Factory.Services.CreateScope();
        _accessEvaluator = scope.ServiceProvider.GetRequiredService<IAccessEvaluator>();
    }

    [Test]
    public async Task EvaluateAsync_NoMembership_ReturnsDenied()
    {
        var otherCompanyId = Guid.NewGuid();
        var request = new AccessRequest(_testUserId, otherCompanyId, "QC", "Products.Read");

        var decision = await _accessEvaluator.EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.ReasonCode, Is.EqualTo("DENIED_MEMBERSHIP"));
    }

    [Test]
    public async Task EvaluateAsync_DirectUserPermission_ReturnsAllowed()
    {
        // Grant direct permission
        await CreateAccessRuleAsync(_testPrincipalId, _productsReadPermId, AccessEffect.Allow);

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read");
        var decision = await _accessEvaluator.EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.True);
        Assert.That(decision.ReasonCode, Is.EqualTo("ALLOWED_DIRECT"));
    }

    [Test]
    public async Task EvaluateAsync_RolePermission_ReturnsAllowed()
    {
        // Create role and assign to user
        var role = await CreateRoleAsync(_companyId, _appId, "Reader", "READER");
        await CreateAuthPrincipalForRoleAsync(role.Id, _companyId, _appId);
        await CreateAccessRuleAsync(role.Id, _productsReadPermId, AccessEffect.Allow);

        var userCompany = await Context.UserCompanies.FirstOrDefaultAsync(uc => uc.UserId == _testUserId);
        userCompany!.AddRole(role.Id);
        await Context.SaveChangesAsync();

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read");
        var decision = await _accessEvaluator.EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.True);
    }

    [Test]
    public async Task EvaluateAsync_DenyBlocksAllow()
    {
        // Grant allow via role
        var role = await CreateRoleAsync(_companyId, _appId, "Editor", "EDITOR");
        await CreateAuthPrincipalForRoleAsync(role.Id, _companyId, _appId);
        await CreateAccessRuleAsync(role.Id, _productsEditPermId, AccessEffect.Allow);

        var userCompany = await Context.UserCompanies.FirstOrDefaultAsync(uc => uc.UserId == _testUserId);
        userCompany!.AddRole(role.Id);
        await Context.SaveChangesAsync();

        // Add deny on same role for same permission
        await CreateAccessRuleAsync(role.Id, _productsEditPermId, AccessEffect.Deny);

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Edit");
        var decision = await _accessEvaluator.EvaluateAsync(request);

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
        var decision = await _accessEvaluator.EvaluateAsync(request);

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
        var decision = await _accessEvaluator.EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.True);
    }

    [Test]
    public async Task EvaluateAsync_NoneScope_RequiresNoScope()
    {
        // Grant permission with ScopeMode.None
        await CreateAccessRuleAsync(_testPrincipalId, _productsReadPermId, AccessEffect.Allow, ScopeMode.None);

        // Request without scope - should work
        var requestNoScope = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read");
        var decisionNoScope = await _accessEvaluator.EvaluateAsync(requestNoScope);
        Assert.That(decisionNoScope.Allowed, Is.True);

        // Request with scope - should fail
        var requestWithScope = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read", "Workshop", "A");
        var decisionWithScope = await _accessEvaluator.EvaluateAsync(requestWithScope);
        Assert.That(decisionWithScope.Allowed, Is.False);
    }

    [Test]
    public async Task EvaluateAsync_MultipleRoles_UnionsPermissions()
    {
        // Role 1: Products.Read
        var role1 = await CreateRoleAsync(_companyId, _appId, "Reader", "READER");
        await CreateAuthPrincipalForRoleAsync(role1.Id, _companyId, _appId);
        await CreateAccessRuleAsync(role1.Id, _productsReadPermId, AccessEffect.Allow);

        // Role 2: Products.Edit
        var role2 = await CreateRoleAsync(_companyId, _appId, "Editor", "EDITOR");
        await CreateAuthPrincipalForRoleAsync(role2.Id, _companyId, _appId);
        await CreateAccessRuleAsync(role2.Id, _productsEditPermId, AccessEffect.Allow);

        var userCompany = await Context.UserCompanies.FirstOrDefaultAsync(uc => uc.UserId == _testUserId);
        userCompany!.AddRole(role1.Id);
        userCompany.AddRole(role2.Id);
        await Context.SaveChangesAsync();

        // Should have both permissions
        var readRequest = new AccessRequest(_testUserId, _companyId, "QC", "Products.Read");
        var readDecision = await _accessEvaluator.EvaluateAsync(readRequest);
        Assert.That(readDecision.Allowed, Is.True);

        var editRequest = new AccessRequest(_testUserId, _companyId, "QC", "Products.Edit");
        var editDecision = await _accessEvaluator.EvaluateAsync(editRequest);
        Assert.That(editDecision.Allowed, Is.True);
    }

    [Test]
    public async Task EvaluateAsync_CompanySuperAdmin_GrantsAll()
    {
        var role = await CreateRoleAsync(_companyId, _appId, "SuperAdmin", "SUPERADMIN", RoleKind.CompanySuperAdmin);
        await CreateAuthPrincipalForRoleAsync(role.Id, _companyId, _appId);

        var userCompany = await Context.UserCompanies.FirstOrDefaultAsync(uc => uc.UserId == _testUserId);
        userCompany!.AddRole(role.Id);
        await Context.SaveChangesAsync();

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Delete");
        var decision = await _accessEvaluator.EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.True);
        Assert.That(decision.ReasonCode, Is.EqualTo("ALLOWED_COMPANY_SUPER_ADMIN"));
    }

    [Test]
    public async Task EvaluateAsync_PrerequisiteGate_EditRequiresRead()
    {
        // Grant Edit but NOT Read
        await CreateAccessRuleAsync(_testPrincipalId, _productsEditPermId, AccessEffect.Allow);

        var request = new AccessRequest(_testUserId, _companyId, "QC", "Products.Edit");
        var decision = await _accessEvaluator.EvaluateAsync(request);

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
        var decision = await _accessEvaluator.EvaluateAsync(request);

        Assert.That(decision.Allowed, Is.True);
    }
}