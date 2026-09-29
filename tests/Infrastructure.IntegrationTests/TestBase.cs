using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Organization;
using CompanyAccessManagement.Infrastructure.Identity;
using CompanyAccessManagement.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyAccessManagement.IntegrationTests;

/// <summary>
/// Each test gets its own host and its own migrated, seeded SQLite file.
/// Arrange steps use <see cref="Context"/>; access decisions are evaluated in a fresh scope.
/// </summary>
public abstract class TestBase
{
    protected SqliteTestWebApplicationFactory Factory { get; private set; } = null!;
    protected IServiceScope Scope { get; private set; } = null!;
    protected IApplicationDbContext Context { get; private set; } = null!;
    protected UserManager<ApplicationUser> UserManager => Scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    [SetUp]
    public virtual async Task SetUp()
    {
        Factory = new SqliteTestWebApplicationFactory();
        await Factory.InitializeAsync();

        Scope = Factory.Services.CreateScope();
        Context = Scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
    }

    [TearDown]
    public virtual void TearDown()
    {
        Scope?.Dispose();
        Factory?.Dispose();
    }

    protected async Task<AccessDecision> EvaluateAsync(AccessRequest request)
    {
        using var scope = Factory.Services.CreateScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IAccessEvaluator>();
        return await evaluator.EvaluateAsync(request);
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
            throw new InvalidOperationException($"Failed to create user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        return user;
    }

    protected async Task<Guid> CreateCompanyAsync(string code, string name)
    {
        var company = new Company(code, name);
        Context.Companies.Add(company);
        await Context.SaveChangesAsync(default);
        return company.Id;
    }

    protected async Task<Guid> CreateApplicationAsync(string code, string name)
    {
        var app = new CompanyAccessManagement.Domain.AccessControl.Application(code, name);
        Context.Applications.Add(app);
        await Context.SaveChangesAsync(default);
        return app.Id;
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
        var exists = await Context.PermissionImplications
            .AnyAsync(pi => pi.PermissionId == permissionId && pi.RequiredPermissionId == requiredPermissionId);

        if (!exists)
        {
            Context.PermissionImplications.Add(new PermissionImplication(permissionId, requiredPermissionId));
            await Context.SaveChangesAsync(default);
        }
    }

    protected async Task<Guid> GetPermissionIdAsync(string resourceCode, string actionCode, string applicationCode = "QC")
    {
        return await (from p in Context.Permissions
                      join r in Context.Resources on p.ResourceId equals r.Id
                      join a in Context.Applications on r.ApplicationId equals a.Id
                      where a.Code == applicationCode && r.Code == resourceCode && p.ActionCode == actionCode
                      select p.Id).SingleAsync();
    }

    /// <summary>
    /// Creates the TEST company and returns identifiers from the QC catalogue seeded at startup
    /// (Products Read/Edit/Delete with Edit requiring Read).
    /// </summary>
    protected async Task<(Guid CompanyId, Guid AppId, Guid ProductsReadPermId, Guid ProductsEditPermId, Guid ProductsDeletePermId)> SeedTestDataAsync()
    {
        var companyId = await CreateCompanyAsync("TEST", "Test Company");
        var appId = await Context.Applications.Where(a => a.Code == "QC").Select(a => a.Id).SingleAsync();

        var productsReadPermId = await GetPermissionIdAsync("Products", "Read");
        var productsEditPermId = await GetPermissionIdAsync("Products", "Edit");
        var productsDeletePermId = await GetPermissionIdAsync("Products", "Delete");

        await CreatePermissionImplicationAsync(productsEditPermId, productsReadPermId);

        return (companyId, appId, productsReadPermId, productsEditPermId, productsDeletePermId);
    }
}
