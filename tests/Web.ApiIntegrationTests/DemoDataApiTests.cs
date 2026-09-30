using CompanyAccessManagement.Application.Common.Models;
using CompanyAccessManagement.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System.Net;
using System.Net.Http.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

/// <summary>
/// The demo workspace is seeded only by a Development host with <c>Demo:Enabled=true</c>, can be re-run safely,
/// and gives the seeded administrator a working company out of the box.
/// </summary>
[TestFixture]
public class DemoDataApiTests : ApiTestBase
{
    [Test]
    public async Task OutsideDevelopment_DemoWorkspace_IsNotSeededEvenWhenEnabled()
    {
        using var host = Factory.WithWebHostBuilder(b => b.UseSetting(DemoDataSeeder.EnabledKey, "true"));

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        (await db.Companies.AnyAsync(c => c.Id == DemoDataSeeder.HoldingId || c.Id == DemoDataSeeder.CompanyId)).ShouldBeFalse();
        (await db.BusinessRoles.AnyAsync(r => r.Code == DemoDataSeeder.RoleCode)).ShouldBeFalse();
    }

    [Test]
    public async Task DevelopmentHost_SeedsDemoWorkspace_IdempotentlyAndUsably()
    {
        using var host = Factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting(DemoDataSeeder.EnabledKey, "true");
        });

        var afterStartup = await CountDemoRowsAsync(host);

        using (var scope = host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();

        var afterSecondRun = await CountDemoRowsAsync(host);
        afterSecondRun.ShouldBe(afterStartup);
        afterStartup.Companies.ShouldBe(2);
        afterStartup.Memberships.ShouldBe(1);
        afterStartup.Roles.ShouldBe(1);
        afterStartup.RoleAssignments.ShouldBe(1);
        afterStartup.RoleRules.ShouldBe(afterStartup.QcPermissions);
        afterStartup.QcPermissions.ShouldBeGreaterThan(0);

        var client = host.CreateClient();
        var login = await client.PostAsJsonAsync("/login", new { email = DemoDataSeeder.AdministratorEmail, password = "Administrator1!" });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await login.Content.ReadFromJsonAsync<DemoLoginResponse>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Company-Id", DemoDataSeeder.CompanyId.ToString());

        (await client.GetAsync("/api/v1/organization/positions")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/organization/personnel")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var roles = await client.GetAsync("/api/v1/access-control/roles");
        roles.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await roles.Content.ReadFromJsonAsync<PaginatedList<RoleListItem>>();
        page!.Items.ShouldContain(r => r.Code == DemoDataSeeder.RoleCode);
    }

    private static async Task<DemoRowCounts> CountDemoRowsAsync(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var roleIds = await db.BusinessRoles.Where(r => r.Code == DemoDataSeeder.RoleCode).Select(r => r.Id).ToListAsync();
        var rolePrincipalIds = await db.AuthPrincipals.Where(p => roleIds.Contains(p.ReferenceId)).Select(p => p.Id).ToListAsync();

        return new DemoRowCounts(
            Companies: await db.Companies.CountAsync(c => c.Id == DemoDataSeeder.HoldingId || c.Id == DemoDataSeeder.CompanyId),
            Memberships: await db.UserCompanies.CountAsync(uc => uc.CompanyId == DemoDataSeeder.CompanyId),
            Roles: roleIds.Count,
            RoleAssignments: await db.UserRoles.CountAsync(ur => roleIds.Contains(ur.RoleId)),
            RoleRules: await db.AccessRules.CountAsync(r => rolePrincipalIds.Contains(r.PrincipalId)),
            QcPermissions: await db.Permissions.CountAsync(p => p.Resource!.Application!.Code == "QC"));
    }

    private record DemoRowCounts(int Companies, int Memberships, int Roles, int RoleAssignments, int RoleRules, int QcPermissions);

    private record RoleListItem(Guid Id, string Code);

    private record DemoLoginResponse(string AccessToken);
}
