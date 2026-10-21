using System.Net;
using System.Text.Json;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Infrastructure.Data;
using CompanyAccessManagement.Testing;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.ApiIntegrationTests;

[TestFixture]
public class EffectiveScopesApiTests : ApiTestBase
{
    private const string Url = "/api/v1/access-control/effective-scopes?applicationCode=QC&permissionCode=Products.Read&scopeType=Workstation";

    [Test]
    public async Task Get_ReturnsSelectedKeys_AndHonorsETag()
    {
        var companyId = await CreateCompanyAsync("EFF", "Effective Scopes");
        var session = await CreateAuthorizedClientAsync("scopes-api@test.com", companyId, "AccessManagement.EffectiveScopes.Read");
        await GrantWorkstationReadAsync(session.UserId, companyId, "F", "V");

        var first = await session.Client.GetAsync(Url);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var etag = first.Headers.ETag;
        etag.ShouldNotBeNull();

        using var body = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("scopeMode").GetString().ShouldBe("Selected");
        body.RootElement.GetProperty("scopeType").GetString().ShouldBe("Workstation");
        body.RootElement.GetProperty("scopeKeys").EnumerateArray().Select(k => k.GetString()).ShouldBe(["F", "V"]);
        body.RootElement.GetProperty("revision").GetString().ShouldBe(etag.Tag.Trim('"'));

        using var cached = new HttpRequestMessage(HttpMethod.Get, Url);
        cached.Headers.IfNoneMatch.Add(etag);
        var notModified = await session.Client.SendAsync(cached);
        notModified.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        (await notModified.Content.ReadAsStringAsync()).ShouldBeEmpty();

        await GrantWorkstationReadAsync(session.UserId, companyId, "S");

        using var stale = new HttpRequestMessage(HttpMethod.Get, Url);
        stale.Headers.IfNoneMatch.Add(etag);
        var refreshed = await session.Client.SendAsync(stale);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        refreshed.Headers.ETag.ShouldNotBeNull();
        refreshed.Headers.ETag!.Tag.ShouldNotBe(etag.Tag);
        using var refreshedBody = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());
        refreshedBody.RootElement.GetProperty("scopeKeys").EnumerateArray().Select(k => k.GetString()).ShouldBe(["F", "S", "V"]);
    }

    [Test]
    public async Task Get_WithoutPermission_IsForbidden()
    {
        var companyId = await CreateCompanyAsync("EFF2", "No Permission");
        var session = await CreateAuthorizedClientAsync("scopes-forbidden@test.com", companyId, "AccessManagement.Role.Read");

        var response = await session.Client.GetAsync(Url);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Get_IgnoresCallerSuppliedUserId()
    {
        var companyId = await CreateCompanyAsync("EFF3", "No User Override");
        var caller = await CreateAuthorizedClientAsync("scopes-caller@test.com", companyId, "AccessManagement.EffectiveScopes.Read");
        var other = await CreateUserAsync("scopes-other@test.com", "Test123!");
        await GrantWorkstationReadAsync(other.Id, companyId, "S", "E");
        await GrantWorkstationReadAsync(caller.UserId, companyId, "F");

        var response = await caller.Client.GetAsync(Url + "&userId=" + other.Id);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("scopeKeys").EnumerateArray().Select(k => k.GetString()).ShouldBe(["F"]);
    }

    private async Task GrantWorkstationReadAsync(Guid userId, Guid companyId, params string[] keys)
    {
        await WithDbAsync(async db =>
        {
            var applicationId = await db.Applications.Where(a => a.Code == "QC").Select(a => a.Id).SingleAsync();
            var permissionId = await (
                from p in db.Permissions
                join r in db.Resources on p.ResourceId equals r.Id
                where r.ApplicationId == applicationId && r.Code == "Products" && p.ActionCode == "Read"
                select p.Id).SingleAsync();
            var membership = await db.UserCompanies.SingleOrDefaultAsync(uc => uc.UserId == userId && uc.CompanyId == companyId);
            AuthPrincipal principal;
            if (membership is null)
            {
                membership = new UserCompany(userId, companyId, Guid.Empty);
                principal = AuthPrincipal.ForUserCompany(membership.Id, companyId, applicationId);
                db.UserCompanies.Add(membership);
                db.Entry(membership).Property(uc => uc.PrincipalId).CurrentValue = principal.Id;
                db.AuthPrincipals.Add(principal);
            }
            else
            {
                principal = await db.AuthPrincipals.SingleAsync(p => p.Id == membership.PrincipalId);
            }

            var rule = new AccessRule(principal.Id, permissionId, AccessEffect.Allow, AccessRuleOrigin.Manual, ScopeMode.Selected);
            foreach (var key in keys)
                rule.AddScope("Workstation", key);
            db.AccessRules.Add(rule);
            await db.SaveChangesAsync(default);
        });
    }
}

[TestFixture]
public class EffectiveScopesTooLargeApiTests : ApiTestBase
{
    [SetUp]
    public override async Task SetUp()
    {
        var factory = new SqliteTestWebApplicationFactory(settings: new Dictionary<string, string?>
        {
            ["EffectiveScopes:MaxKeys"] = "1"
        });
        await factory.InitializeAsync();
        Factory = factory;
        Client = Factory.CreateClient();
    }

    [Test]
    public async Task MoreKeysThanTheCeiling_Returns409()
    {
        var companyId = await CreateCompanyAsync("BIG", "Too Many Scopes");
        var session = await CreateAuthorizedClientAsync("scopes-large@test.com", companyId, "AccessManagement.EffectiveScopes.Read");
        await WithDbAsync(async db =>
        {
            var applicationId = await db.Applications.Where(a => a.Code == "QC").Select(a => a.Id).SingleAsync();
            var permissionId = await (
                from p in db.Permissions
                join r in db.Resources on p.ResourceId equals r.Id
                where r.ApplicationId == applicationId && r.Code == "Products" && p.ActionCode == "Read"
                select p.Id).SingleAsync();
            var principalId = await db.UserCompanies.Where(uc => uc.UserId == session.UserId).Select(uc => uc.PrincipalId).SingleAsync();
            var rule = new AccessRule(principalId, permissionId, AccessEffect.Allow, AccessRuleOrigin.Manual, ScopeMode.Selected);
            rule.AddScope("Workstation", "F");
            rule.AddScope("Workstation", "V");
            db.AccessRules.Add(rule);
            await db.SaveChangesAsync(default);
        });

        var response = await session.Client.GetAsync(
            "/api/v1/access-control/effective-scopes?applicationCode=QC&permissionCode=Products.Read&scopeType=Workstation");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var code = body.RootElement.TryGetProperty("code", out var direct) ? direct.GetString()
            : body.RootElement.GetProperty("extensions").GetProperty("code").GetString();
        code.ShouldBe("EFFECTIVE_SCOPES_TOO_LARGE");
    }
}
