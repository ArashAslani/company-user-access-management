using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Organization;
using CompanyAccessManagement.Infrastructure.Authorization;
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
using System.Net;
using System.Net.Http.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

public abstract class ApiTestBase
{
    public ApiTestWebApplicationFactory Factory { get; protected set; } = null!;
    public IServiceScope Scope { get; protected set; } = null!;
    public IApplicationDbContext Context { get; protected set; } = null!;
    public UserManager<ApplicationUser> UserManager { get; protected set; } = null!;
    public RoleManager<IdentityRole<Guid>> RoleManager { get; protected set; } = null!;
    public HttpClient Client { get; protected set; } = null!;

    [SetUp]
    public virtual async Task SetUp()
    {
        Factory = new ApiTestWebApplicationFactory();
        Scope = Factory.Services.CreateScope();
        Context = Scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        UserManager = Scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        RoleManager = Scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        Client = Factory.CreateClient();

        // Clean database
        await CleanDatabaseAsync();
    }

    [TearDown]
    public virtual void TearDown()
    {
        Client?.Dispose();
        UserManager?.Dispose();
        RoleManager?.Dispose();
        Scope?.Dispose();
        Factory?.Dispose();
    }

    protected async Task CleanDatabaseAsync()
    {
        if (Context is ApplicationDbContext dbContext)
        {
            var tables = new[]
            {
                "\"auth\".\"RuleScopes\"",
                "\"auth\".\"AccessRules\"",
                "\"auth\".\"UserRoles\"",
                "\"auth\".\"AuthPrincipals\"",
                "\"auth\".\"PermissionImplications\"",
                "\"auth\".\"Roles\"",
                "\"auth\".\"Permissions\"",
                "\"auth\".\"Resources\"",
                "\"auth\".\"Applications\"",
                "\"identity\".\"AspNetUserRoles\"",
                "\"identity\".\"AspNetUserClaims\"",
                "\"identity\".\"AspNetUserLogins\"",
                "\"identity\".\"AspNetUserTokens\"",
                "\"identity\".\"AspNetRoleClaims\"",
                "\"identity\".\"AspNetRoles\"",
                "\"identity\".\"AspNetUsers\"",
                "\"org\".\"PersonnelPositions\"",
                "\"org\".\"UserCompanies\"",
                "\"org\".\"Personnel\"",
                "\"org\".\"Positions\"",
                "\"org\".\"Companies\"",
            };

            foreach (var table in tables)
            {
                try
                {
#pragma warning disable EF1002
                    await dbContext.Database.ExecuteSqlRawAsync($"DELETE FROM {table}");
#pragma warning restore EF1002
                }
                catch
                {
                    // Table might not exist, ignore
                }
            }
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

protected async Task<string> LoginAsync(string email, string password)
    {
        // Try Identity API login endpoint first
        var apiRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Users/login")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("email", email),
                new KeyValuePair<string, string>("password", password),
            })
        };

        var apiResponse = await Client.SendAsync(apiRequest);
        if (apiResponse.IsSuccessStatusCode)
        {
            var content = await apiResponse.Content.ReadFromJsonAsync<LoginResponse>();
            return content!.Token;
        }

        // Try cookie-based login as fallback
        var cookieRequest = new HttpRequestMessage(HttpMethod.Post, "/Identity/Account/Login")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("Input.Email", email),
                new KeyValuePair<string, string>("Input.Password", password),
            })
        };

        var cookieResponse = await Client.SendAsync(cookieRequest);
        if (cookieResponse.IsSuccessStatusCode)
        {
            var cookies = cookieResponse.Headers.GetValues("Set-Cookie").ToList();
            return string.Join("; ", cookies);
        }

        // If both fail, try JSON format for API login
        var jsonRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Users/login")
        {
            Content = JsonContent.Create(new { email, password })
        };

        var jsonResponse = await Client.SendAsync(jsonRequest);
        if (jsonResponse.IsSuccessStatusCode)
        {
            var jsonContent = await jsonResponse.Content.ReadFromJsonAsync<LoginResponse>();
            return jsonContent!.Token;
        }

        // If all fail, throw
        apiResponse.EnsureSuccessStatusCode();
        return "";
    }

    protected void SetAuthHeader(string token)
    {
        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
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

    protected async Task<Guid> CreateRoleAsync(Guid companyId, Guid applicationId, string name, string code, RoleKind kind = RoleKind.Standard, Guid? parentRoleId = null)
    {
        var role = new Role(companyId, applicationId, code, name, kind, parentRoleId);
        Context.Roles.Add(role);
        await Context.SaveChangesAsync(default);
        return role.Id;
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

    protected async Task<(Guid CompanyId, Guid AppId, Guid ProductsReadPermId, Guid ProductsEditPermId, Guid ProductsDeletePermId)> SeedTestDataAsync()
    {
        // Create test company (check if exists first)
        var company = await Context.Companies.FirstOrDefaultAsync(c => c.Code == "TEST");
        Guid companyId;
        if (company == null)
        {
            companyId = await CreateCompanyAsync("TEST", "Test Company");
        }
        else
        {
            companyId = company.Id;
        }

        // Create test application (check if exists first)
        var app = await Context.Applications.FirstOrDefaultAsync(a => a.Code == "QC");
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
        var productsResource = await Context.Resources.FirstOrDefaultAsync(r => r.Code == "Products" && r.ApplicationId == appId);
        if (productsResource == null)
        {
            productsResource = new Resource(appId, "Products", "Products");
            Context.Resources.Add(productsResource);
            await Context.SaveChangesAsync(default);
        }

        var productsReadPerm = await Context.Permissions.FirstOrDefaultAsync(p => p.ResourceId == productsResource.Id && p.ActionCode == "Read");
        Guid productsReadPermId;
        if (productsReadPerm == null)
        {
            productsReadPermId = await CreatePermissionAsync(productsResource.Id, "Read");
        }
        else
        {
            productsReadPermId = productsReadPerm.Id;
        }

        var productsEditPerm = await Context.Permissions.FirstOrDefaultAsync(p => p.ResourceId == productsResource.Id && p.ActionCode == "Edit");
        Guid productsEditPermId;
        if (productsEditPerm == null)
        {
            productsEditPermId = await CreatePermissionAsync(productsResource.Id, "Edit");
        }
        else
        {
            productsEditPermId = productsEditPerm.Id;
        }

        var productsDeletePerm = await Context.Permissions.FirstOrDefaultAsync(p => p.ResourceId == productsResource.Id && p.ActionCode == "Delete");
        Guid productsDeletePermId;
        if (productsDeletePerm == null)
        {
            productsDeletePermId = await CreatePermissionAsync(productsResource.Id, "Delete");
        }
        else
        {
            productsDeletePermId = productsDeletePerm.Id;
        }

        var labResource = await Context.Resources.FirstOrDefaultAsync(r => r.Code == "Laboratory" && r.ApplicationId == appId);
        if (labResource == null)
        {
            labResource = new Resource(appId, "Laboratory", "Laboratory");
            Context.Resources.Add(labResource);
            await Context.SaveChangesAsync(default);
        }

        var labReadPerm = await Context.Permissions.FirstOrDefaultAsync(p => p.ResourceId == labResource.Id && p.ActionCode == "Read");
        if (labReadPerm == null)
        {
            await CreatePermissionAsync(labResource.Id, "Read");
        }

        var labApprovePerm = await Context.Permissions.FirstOrDefaultAsync(p => p.ResourceId == labResource.Id && p.ActionCode == "Approve");
        if (labApprovePerm == null)
        {
            await CreatePermissionAsync(labResource.Id, "Approve");
        }

        // Create permission implication: Edit requires Read
        await CreatePermissionImplicationAsync(productsEditPermId, productsReadPermId);

        return (companyId, appId, productsReadPermId, productsEditPermId, productsDeletePermId);
    }

    protected async Task CreatePermissionImplicationAsync(Guid permissionId, Guid requiredPermissionId)
    {
        var exists = await Context.PermissionImplications
            .AnyAsync(pi => pi.PermissionId == permissionId && pi.RequiredPermissionId == requiredPermissionId);

        if (!exists)
        {
            var implication = new PermissionImplication(permissionId, requiredPermissionId);
            Context.PermissionImplications.Add(implication);
            await Context.SaveChangesAsync(default);
        }
    }

    private record LoginResponse(string Token);
}

public class ApiTestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public ApiTestWebApplicationFactory()
    {
        _connectionString = $"DataSource=file:test-{Guid.NewGuid()}?mode=memory&cache=shared;Foreign Keys=True";
    }

protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    // Use Development environment to ensure:
    // 1. AddInfrastructureServices() is called (registers Identity API endpoints)
    builder.UseEnvironment("Development");

    builder.ConfigureTestServices(services =>
    {
        // Replace the database connection with our in-memory SQLite
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
        if (descriptor != null)
            services.Remove(descriptor);

        var efCoreSqliteDescriptors = services.Where(d => d.ServiceType.FullName?.Contains("Microsoft.EntityFrameworkCore.Sqlite") == true).ToList();
        foreach (var d in efCoreSqliteDescriptors)
            services.Remove(d);

        services.AddSingleton<Microsoft.AspNetCore.Identity.IEmailSender<ApplicationUser>, DummyEmailSender>();

        services.AddDataProtection();

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlite(_connectionString);
        });
    });

    // Ensure database schema is created after host is built
    builder.Configure(async app =>
    {
        using var scope = app.ApplicationServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
    });
    }

    private class DummyEmailSender : Microsoft.AspNetCore.Identity.IEmailSender<ApplicationUser>
    {
        public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) => Task.CompletedTask;
        public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) => Task.CompletedTask;
        public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) => Task.CompletedTask;
    }
}