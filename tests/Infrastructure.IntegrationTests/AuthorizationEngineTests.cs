using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Domain.AccessControl;
using CleanArchitecture.Domain.Organization;
using CleanArchitecture.Infrastructure.Authorization;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;

namespace CleanArchitecture.IntegrationTests;

[TestFixture]
public class AuthorizationEngineTests : TestBase
{
    private IAccessEvaluator _accessEvaluator = null!;
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

        // Create user company
        var userCompany = await CreateUserCompanyAsync(_testUserId, _companyId, Guid.NewGuid());
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
        
        // Debug: Check what's in the database
        var userCompany = await Context.UserCompanies.FirstOrDefaultAsync(uc => uc.UserId == _testUserId);
        if (userCompany != null)
        {
            Console.WriteLine($"UserCompany: {userCompany.Id}, PrincipalId: {userCompany.PrincipalId}, Status: {userCompany.Status}");
        }
        else
        {
            Console.WriteLine("UserCompany: NULL");
        }
        
        var permissions = await Context.Permissions.Include(p => p.Resource).ToListAsync();
        foreach (var p in permissions)
        {
            Console.WriteLine($"Permission: {p.Id}, Resource: {p.Resource?.Code}, Action: {p.ActionCode}");
        }
        
        var rules = await Context.AccessRules.Include(r => r.Scopes).ToListAsync();
        foreach (var r in rules)
        {
            Console.WriteLine($"AccessRule: PrincipalId={r.PrincipalId}, PermissionId={r.PermissionId}, Effect={r.Effect}, ScopeMode={r.ScopeMode}");
        }

        var decision = await _accessEvaluator.EvaluateAsync(request);

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
        var decision = await _accessEvaluator.EvaluateAsync(request);

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
        await Context.SaveChangesAsync(default);

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

// ============================================================================
// TestBase - Shared test infrastructure
// ============================================================================

public abstract class TestBase
{
    protected TestWebApplicationFactory Factory { get; private set; } = null!;
    protected IServiceScope Scope { get; private set; } = null!;
    protected IApplicationDbContext Context { get; private set; } = null!;
    protected UserManager<ApplicationUser> UserManager { get; private set; } = null!;
    protected RoleManager<IdentityRole<Guid>> RoleManager { get; private set; } = null!;

    [SetUp]
    public virtual async Task SetUp()
    {
        Factory = new TestWebApplicationFactory();
        Scope = Factory.Services.CreateScope();
        Context = Scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        UserManager = Scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        RoleManager = Scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        // Clean database
        await CleanDatabaseAsync();
    }

    [TearDown]
    public virtual void TearDown()
    {
        UserManager?.Dispose();
        RoleManager?.Dispose();
        Scope.Dispose();
        Factory.Dispose();
    }

    protected async Task CleanDatabaseAsync()
    {
        // For InMemory database, recreate the database entirely
        if (Context is ApplicationDbContext dbContext)
        {
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.Database.EnsureCreatedAsync();
        }
    }

    protected async Task<ApplicationUser> CreateUserAsync(string email, string password, bool isActive = true)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            IsActive = isActive
        };

        var result = await UserManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new Exception($"Failed to create user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        return user;
    }

    protected async Task<Guid> CreateCompanyAsync(string code, string name)
    {
        var company = new CleanArchitecture.Domain.Organization.Company(code, name);
        Context.Companies.Add(company);
        await Context.SaveChangesAsync(default);
        return company.Id;
    }

    protected async Task<Guid> CreateApplicationAsync(string code, string name)
    {
        var app = new CleanArchitecture.Domain.AccessControl.Application(code, name);
        Context.Applications.Add(app);
        await Context.SaveChangesAsync(default);
        return app.Id;
    }

    protected async Task<Guid> CreateResourceAsync(Guid applicationId, string code, string name)
    {
        var resource = new Resource(applicationId, code, name);
        Context.Resources.Add(resource);
        await Context.SaveChangesAsync(default);
        return resource.Id;
    }

    protected async Task<Guid> CreatePermissionAsync(Guid resourceId, string actionCode, string description = "")
    {
        var permission = new Permission(resourceId, actionCode, description);
        Context.Permissions.Add(permission);
        await Context.SaveChangesAsync(default);
        return permission.Id;
    }

    protected async Task<Role> CreateRoleAsync(Guid companyId, Guid applicationId, string name, string code, RoleKind kind = RoleKind.Standard, Guid? parentRoleId = null)
    {
        var role = new Role(companyId, applicationId, code, name, kind, parentRoleId);
        Context.Roles.Add(role);
        await Context.SaveChangesAsync(default);
        return role;
    }

    protected async Task<UserCompany> CreateUserCompanyAsync(Guid userId, Guid companyId, Guid principalId)
    {
        var userCompany = new UserCompany(userId, companyId, principalId);
        Context.UserCompanies.Add(userCompany);
        await Context.SaveChangesAsync(default);
        return userCompany;
    }

    protected async Task<UserRole> CreateUserRoleAsync(Guid userCompanyId, Guid roleId)
    {
        var userRole = new UserRole(userCompanyId, roleId);
        Context.UserRoles.Add(userRole);
        await Context.SaveChangesAsync(default);
        return userRole;
    }

    protected async Task<AccessRule> CreateAccessRuleAsync(Guid principalId, Guid permissionId, AccessEffect effect, ScopeMode scopeMode = ScopeMode.None, string? scopeType = null, string? scopeKey = null, AccessRuleOrigin origin = AccessRuleOrigin.Manual)
    {
        var rule = new AccessRule(principalId, permissionId, effect, origin, scopeMode);
        if (scopeMode == ScopeMode.Selected && !string.IsNullOrEmpty(scopeType) && !string.IsNullOrEmpty(scopeKey))
        {
            rule.AddScope(scopeType, scopeKey);
        }
        Context.AccessRules.Add(rule);
        await Context.SaveChangesAsync(default);
        return rule;
    }

    protected async Task<AuthPrincipal> CreateAuthPrincipalForUserCompanyAsync(Guid userCompanyId, Guid companyId, Guid applicationId)
    {
        var principal = new AuthPrincipal(PrincipalType.UserCompany, userCompanyId, companyId, applicationId);
        Context.AuthPrincipals.Add(principal);
        await Context.SaveChangesAsync(default);
        return principal;
    }

    protected async Task<AuthPrincipal> CreateAuthPrincipalForRoleAsync(Guid roleId, Guid companyId, Guid applicationId)
    {
        var principal = new AuthPrincipal(PrincipalType.Role, roleId, companyId, applicationId);
        Context.AuthPrincipals.Add(principal);
        await Context.SaveChangesAsync(default);
        return principal;
    }

    protected async Task CreatePermissionImplicationAsync(Guid permissionId, Guid requiredPermissionId)
    {
        var implication = new PermissionImplication(permissionId, requiredPermissionId);
        Context.PermissionImplications.Add(implication);
        await Context.SaveChangesAsync(default);
    }

protected async Task<(Guid CompanyId, Guid AppId, Guid ProductsReadPermId, Guid ProductsEditPermId, Guid ProductsDeletePermId)> SeedTestDataAsync()
    {
        // Create test company
        var companyId = await CreateCompanyAsync("TEST", "Test Company");

        // Create test application
        var appId = await CreateApplicationAsync("QC", "Quality Control");

        // Create resources and permissions
        var productsResource = await CreateResourceAsync(appId, "Products", "Products");
        var productsReadPermId = await CreatePermissionAsync(productsResource, "Read");
        var productsEditPermId = await CreatePermissionAsync(productsResource, "Edit");
        var productsDeletePermId = await CreatePermissionAsync(productsResource, "Delete");

        var labResource = await CreateResourceAsync(appId, "Laboratory", "Laboratory");
        var labRead = await CreatePermissionAsync(labResource, "Read");
        var labApprove = await CreatePermissionAsync(labResource, "Approve");

        // Create permission implication: Edit requires Read
        await CreatePermissionImplicationAsync(productsEditPermId, productsReadPermId);

        return (companyId, appId, productsReadPermId, productsEditPermId, productsDeletePermId);
    }
}

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        
        builder.ConfigureTestServices(services =>
        {
            // Remove the existing DbContext registration
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            // Remove any EntityFrameworkCore.Sqlite services
            var efCoreSqliteDescriptors = services.Where(d => d.ServiceType.FullName?.Contains("Microsoft.EntityFrameworkCore.Sqlite") == true).ToList();
            foreach (var d in efCoreSqliteDescriptors)
                services.Remove(d);

            // Register dummy email sender for Identity API
            services.AddSingleton<Microsoft.AspNetCore.Identity.IEmailSender<ApplicationUser>, DummyEmailSender>();

            // Add Data Protection for Identity
            services.AddDataProtection();

            // Use in-memory database for tests
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase("TestDb");
            });

            // Register Identity services
            services.AddIdentityCore<ApplicationUser>()
                .AddRoles<IdentityRole<Guid>>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            // Register IApplicationDbContext
            services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

            // Register IAccessEvaluator
            services.AddScoped<IAccessEvaluator, AccessEvaluator>();

            // Ensure the database is created
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();
        });
    }

    // Dummy email sender for testing
    private class DummyEmailSender : Microsoft.AspNetCore.Identity.IEmailSender<ApplicationUser>
    {
        public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
            => Task.CompletedTask;

        public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
            => Task.CompletedTask;

        public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
            => Task.CompletedTask;
    }
}