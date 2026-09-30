using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Organization;
using CompanyAccessManagement.Infrastructure.Authorization;
using CompanyAccessManagement.Infrastructure.Data;
using CompanyAccessManagement.Infrastructure.Identity;
using CompanyAccessManagement.Testing;
using CompanyAccessManagement.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Json;
using System.IO;
using System.Linq;

namespace CompanyAccessManagement.ApiIntegrationTests;

public abstract class ApiTestBase
{
    public WebApplicationFactory<Program> Factory { get; protected set; } = null!;
    public HttpClient Client { get; protected set; } = null!;

    [SetUp]
    public virtual async Task SetUp()
    {
        var factory = new SqliteTestWebApplicationFactory();
        await factory.InitializeAsync();
        Factory = factory;
        Client = Factory.CreateClient();
    }

    [TearDown]
    public virtual void TearDown()
    {
        Client?.Dispose();
        Factory?.Dispose();
    }

    protected async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await action(db);
    }

    protected async Task WithDbAsync(Func<ApplicationDbContext, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await action(db);
    }

    protected async Task<ApplicationUser> CreateUserAsync(string email, string password, bool isActive = true)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            IsActive = isActive
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new Exception($"Failed to create user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        return user;
    }

    protected async Task<string> LoginAsync(string email, string password)
    {
        var response = await Client.PostAsJsonAsync("/login", new { email, password });
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (string.IsNullOrEmpty(content?.AccessToken))
            throw new InvalidOperationException("/login did not return an access token.");

        return content.AccessToken;
    }

    protected void SetAuthHeader(string token)
    {
        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    protected void SetCompanyHeader(Guid companyId)
    {
        Client.DefaultRequestHeaders.Remove("X-Company-Id");
        Client.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString());
    }

    protected async Task<Guid> CreateCompanyAsync(string code, string name)
    {
        return await WithDbAsync(async db =>
        {
            var company = new Company(code, name);
            db.Companies.Add(company);
            await db.SaveChangesAsync(default);
            return company.Id;
        });
    }

    protected async Task<Guid> CreateApplicationAsync(string code, string name)
    {
        return await WithDbAsync(async db =>
        {
            var app = new CompanyAccessManagement.Domain.AccessControl.Application(code, name);
            db.Applications.Add(app);
            await db.SaveChangesAsync(default);
            return app.Id;
        });
    }

    protected async Task<Guid> CreateResourceAsync(Guid applicationId, string code, string name)
    {
        return await WithDbAsync(async db =>
        {
            var resource = new Resource(applicationId, code, name);
            db.Resources.Add(resource);
            await db.SaveChangesAsync(default);
            return resource.Id;
        });
    }

    protected async Task<Guid> CreatePermissionAsync(Guid resourceId, string actionCode, string description = "")
    {
        return await WithDbAsync(async db =>
        {
            var permission = new Permission(resourceId, actionCode, description);
            db.Permissions.Add(permission);
            await db.SaveChangesAsync(default);
            return permission.Id;
        });
    }

    protected async Task<Guid> CreateRoleAsync(Guid companyId, Guid applicationId, string name, string code, RoleKind kind = RoleKind.Standard, Guid? parentRoleId = null)
    {
        return await WithDbAsync(async db =>
        {
            var role = new Role(companyId, applicationId, code, name, kind, parentRoleId);
            db.BusinessRoles.Add(role);
            db.AuthPrincipals.Add(AuthPrincipal.ForRole(role.Id, companyId, applicationId));
            await db.SaveChangesAsync(default);
            return role.Id;
        });
    }

    protected async Task<UserCompany> CreateUserCompanyAsync(Guid userId, Guid companyId, Guid principalId)
    {
        return await WithDbAsync(async db =>
        {
            var userCompany = new UserCompany(userId, companyId, principalId);
            db.UserCompanies.Add(userCompany);
            await db.SaveChangesAsync(default);
            return userCompany;
        });
    }

    protected async Task<UserRole> CreateUserRoleAsync(Guid userCompanyId, Guid roleId)
    {
        return await WithDbAsync(async db =>
        {
            var userRole = new UserRole(userCompanyId, roleId);
            db.UserRoles.Add(userRole);
            await db.SaveChangesAsync(default);
            return userRole;
        });
    }

    protected async Task<AccessRule> CreateAccessRuleAsync(Guid principalId, Guid permissionId, AccessEffect effect, ScopeMode scopeMode = ScopeMode.None, string? scopeType = null, string? scopeKey = null, AccessRuleOrigin origin = AccessRuleOrigin.Manual)
    {
        return await WithDbAsync(async db =>
        {
            var rule = new AccessRule(principalId, permissionId, effect, origin, scopeMode);
            if (scopeMode == ScopeMode.Selected && !string.IsNullOrEmpty(scopeType) && !string.IsNullOrEmpty(scopeKey))
            {
                rule.AddScope(scopeType, scopeKey);
            }
            db.AccessRules.Add(rule);
            await db.SaveChangesAsync(default);
            return rule;
        });
    }

    protected async Task<AuthPrincipal> CreateAuthPrincipalForUserCompanyAsync(Guid userCompanyId, Guid companyId, Guid applicationId)
    {
        return await WithDbAsync(async db =>
        {
            var principal = new AuthPrincipal(PrincipalType.UserCompany, userCompanyId, companyId, applicationId);
            db.AuthPrincipals.Add(principal);
            await db.SaveChangesAsync(default);
            return principal;
        });
    }

    protected async Task<AuthPrincipal> CreateAuthPrincipalForRoleAsync(Guid roleId, Guid companyId, Guid applicationId)
    {
        return await WithDbAsync(async db =>
        {
            var principal = new AuthPrincipal(PrincipalType.Role, roleId, companyId, applicationId);
            db.AuthPrincipals.Add(principal);
            await db.SaveChangesAsync(default);
            return principal;
        });
    }

    protected async Task<(Guid CompanyId, Guid AppId, Guid ProductsReadPermId, Guid ProductsEditPermId, Guid ProductsDeletePermId)> SeedTestDataAsync()
    {
        return await WithDbAsync(async db =>
        {
            // Create test company
            var company = await db.Companies.FirstOrDefaultAsync(c => c.Code == "TEST");
            Guid companyId;
            if (company == null)
            {
                companyId = await CreateCompanyAsync("TEST", "Test Company");
            }
            else
            {
                companyId = company.Id;
            }

            // Create test application
            var app = await db.Applications.FirstOrDefaultAsync(a => a.Code == "QC");
            Guid appId;
            if (app == null)
            {
                appId = await CreateApplicationAsync("QC", "Quality Control");
            }
            else
            {
                appId = app.Id;
            }

            // Create resources and permissions (check if exist first)
            var productsResource = await db.Resources.FirstOrDefaultAsync(r => r.Code == "Products" && r.ApplicationId == appId);
            if (productsResource == null)
            {
                productsResource = new Resource(appId, "Products", "Products");
                db.Resources.Add(productsResource);
                await db.SaveChangesAsync(default);
            }

            var productsReadPerm = await db.Permissions.FirstOrDefaultAsync(p => p.ResourceId == productsResource.Id && p.ActionCode == "Read");
            Guid productsReadPermId;
            if (productsReadPerm == null)
            {
                productsReadPermId = await CreatePermissionAsync(productsResource.Id, "Read");
            }
            else
            {
                productsReadPermId = productsReadPerm.Id;
            }

            var productsEditPerm = await db.Permissions.FirstOrDefaultAsync(p => p.ResourceId == productsResource.Id && p.ActionCode == "Edit");
            Guid productsEditPermId;
            if (productsEditPerm == null)
            {
                productsEditPermId = await CreatePermissionAsync(productsResource.Id, "Edit");
            }
            else
            {
                productsEditPermId = productsEditPerm.Id;
            }

            var productsDeletePerm = await db.Permissions.FirstOrDefaultAsync(p => p.ResourceId == productsResource.Id && p.ActionCode == "Delete");
            Guid productsDeletePermId;
            if (productsDeletePerm == null)
            {
                productsDeletePermId = await CreatePermissionAsync(productsResource.Id, "Delete");
            }
            else
            {
                productsDeletePermId = productsDeletePerm.Id;
            }

            var labResource = await db.Resources.FirstOrDefaultAsync(r => r.Code == "Laboratory" && r.ApplicationId == appId);
            if (labResource == null)
            {
                labResource = new Resource(appId, "Laboratory", "Laboratory");
                db.Resources.Add(labResource);
                await db.SaveChangesAsync(default);
            }

            var labReadPerm = await db.Permissions.FirstOrDefaultAsync(p => p.ResourceId == labResource.Id && p.ActionCode == "Read");
            if (labReadPerm == null)
            {
                await CreatePermissionAsync(labResource.Id, "Read");
            }

            var labApprovePerm = await db.Permissions.FirstOrDefaultAsync(p => p.ResourceId == labResource.Id && p.ActionCode == "Approve");
            if (labApprovePerm == null)
            {
                await CreatePermissionAsync(labResource.Id, "Approve");
            }

            // Create permission implication: Edit requires Read
            await CreatePermissionImplicationAsync(productsEditPermId, productsReadPermId);

            return (companyId, appId, productsReadPermId, productsEditPermId, productsDeletePermId);
        });
    }

    protected async Task CreatePermissionImplicationAsync(Guid permissionId, Guid requiredPermissionId)
    {
        await WithDbAsync(async db =>
        {
            var exists = await db.PermissionImplications
                .AnyAsync(pi => pi.PermissionId == permissionId && pi.RequiredPermissionId == requiredPermissionId);

            if (!exists)
            {
                var implication = new PermissionImplication(permissionId, requiredPermissionId);
                db.PermissionImplications.Add(implication);
                await db.SaveChangesAsync(default);
            }
        });
    }

    /// <summary>
    /// Creates a user who is an active member of <paramref name="companyId"/>, grants it an Allow rule for each
    /// permission, logs in, and returns a client carrying the bearer token and X-Company-Id header.
    /// </summary>
    /// <param name="permissions">Full permission codes as the endpoints require them, e.g. "Organization.Position.Read".</param>
    protected Task<AuthSession> CreateAuthorizedClientAsync(Guid companyId, params string[] permissions)
        => CreateAuthorizedClientAsync("test@test.com", companyId, permissions);

    /// <param name="email">Distinct per session when a test needs several users.</param>
    protected async Task<AuthSession> CreateAuthorizedClientAsync(string email, Guid companyId, params string[] permissions)
    {
        const string password = "Test123!";

        var user = await CreateUserAsync(email, password);

        var (principalId, grantedPermissions) = await WithDbAsync(async db =>
        {
            var qcApp = await db.Applications.SingleAsync(a => a.Code == "QC");

            var userCompany = new UserCompany(user.Id, companyId, Guid.Empty);
            var principal = AuthPrincipal.ForUserCompany(userCompany.Id, companyId, qcApp.Id);
            db.UserCompanies.Add(userCompany);
            db.Entry(userCompany).Property(uc => uc.PrincipalId).CurrentValue = principal.Id;
            db.AuthPrincipals.Add(principal);

            var granted = new Dictionary<string, Guid>();
            foreach (var code in permissions)
            {
                var permissionId = await ResolvePermissionIdAsync(db, qcApp.Id, code);
                db.AccessRules.Add(new AccessRule(principal.Id, permissionId, AccessEffect.Allow, AccessRuleOrigin.Manual, ScopeMode.None));
                granted[code] = permissionId;
            }

            await db.SaveChangesAsync(default);
            return (principal.Id, granted);
        });

        var authToken = await LoginAsync(email, password);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);
        client.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString());

        return new AuthSession(client, user.Id, principalId, grantedPermissions);
    }

    /// <summary>
    /// Creates a root standard role with an ALLOW rule of <paramref name="scopeMode"/> for each permission and assigns it to the
    /// session's membership, so it is the actor's managing role (design §32) for roles created beneath it.
    /// </summary>
    protected async Task<ManagingRole> CreateManagingRoleAsync(AuthSession session, Guid companyId, string code, ScopeMode scopeMode, params Guid[] permissionIds)
    {
        return await WithDbAsync(async db =>
        {
            var qcApp = await db.Applications.SingleAsync(a => a.Code == "QC");
            var userCompanyId = await db.UserCompanies.Where(uc => uc.PrincipalId == session.PrincipalId).Select(uc => uc.Id).SingleAsync();

            var role = new Role(companyId, qcApp.Id, code, code);
            var principal = AuthPrincipal.ForRole(role.Id, companyId, qcApp.Id);
            db.BusinessRoles.Add(role);
            db.AuthPrincipals.Add(principal);
            foreach (var permissionId in permissionIds)
                db.AccessRules.Add(new AccessRule(principal.Id, permissionId, AccessEffect.Allow, AccessRuleOrigin.Manual, scopeMode));
            db.UserRoles.Add(new UserRole(userCompanyId, role.Id));

            await db.SaveChangesAsync(default);
            return new ManagingRole(role.Id, principal.Id);
        });
    }

    private static async Task<Guid> ResolvePermissionIdAsync(ApplicationDbContext db, Guid applicationId, string permissionCode)
    {
        var separator = permissionCode.IndexOf('.');
        if (separator <= 0 || separator == permissionCode.Length - 1)
            throw new ArgumentException($"Permission code '{permissionCode}' must be in the form 'Resource.Action'.", nameof(permissionCode));

        var resourceCode = permissionCode[..separator];
        var actionCode = permissionCode[(separator + 1)..];

        var permissionId = await (from p in db.Permissions
                                  join r in db.Resources on p.ResourceId equals r.Id
                                  where r.ApplicationId == applicationId && r.Code == resourceCode && p.ActionCode == actionCode
                                  select (Guid?)p.Id).FirstOrDefaultAsync();

        return permissionId ?? throw new InvalidOperationException(
            $"Permission '{permissionCode}' is not seeded (resource '{resourceCode}', action '{actionCode}').");
    }

    private record LoginResponse(string AccessToken, string TokenType, int ExpiresIn, string RefreshToken);
}

public record AuthSession(HttpClient Client, Guid UserId, Guid PrincipalId, Dictionary<string, Guid> GrantedPermissions);

public record ManagingRole(Guid RoleId, Guid PrincipalId);
