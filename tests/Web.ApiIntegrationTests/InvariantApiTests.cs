using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;
using System.Net;
using System.Net.Http.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

/// <summary>
/// Parallel API races, end-to-end cache invalidation through the real pipeline, and a final cross-tenant sweep.
/// </summary>
[TestFixture]
public class InvariantApiTests : ApiTestBase
{
    private Guid _companyId;
    private Guid _appId;
    private Guid _otherCompanyId;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _appId, _, _, _) = await SeedTestDataAsync();
        _otherCompanyId = await CreateCompanyAsync("OTHER", "Other Company");
    }

    [Test]
    public async Task ConcurrentPrimary_SameCompany_AtMostOnePrimaryWins()
    {
        var session = await CreateAuthorizedClientAsync(_companyId,
            "Organization.Personnel.Create", "Organization.Position.Create", "Organization.PersonnelPosition.Create");
        var personnelId = await CreatePersonnelAsync(session);
        var first = await CreatePositionAsync(session, "P1");
        var second = await CreatePositionAsync(session, "P2");

        var responses = await Task.WhenAll(
            AssignPrimaryAsync(session, personnelId, first),
            AssignPrimaryAsync(session, personnelId, second));

        foreach (var response in responses)
            ((int)response.StatusCode).ShouldBeOneOf(201, 409);

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);

        var primaries = await WithDbAsync(db => db.PersonnelPositions
            .Where(pp => pp.PersonnelId == personnelId && pp.IsPrimary && pp.Status == PersonnelPositionStatus.Active)
            .ToListAsync());
        primaries.Count.ShouldBe(1);
        (await responses.Single(r => r.StatusCode == HttpStatusCode.Conflict).Content.ReadAsStringAsync())
            .ShouldMatch(@"(PRIMARY_OVERLAP_CONFLICT|CONCURRENCY_CONFLICT)");
    }

    [Test]
    public async Task ConcurrentPositionHierarchyMutation_CannotCreateCycle()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Read", "Organization.Position.Create", "Organization.Position.Edit");
        var a = await CreatePositionAsync(session, "A");
        var b = await CreatePositionAsync(session, "B");

        var responses = await Task.WhenAll(
            UpdatePositionParentAsync(session, a, "A", b),
            UpdatePositionParentAsync(session, b, "B", a));

        foreach (var response in responses)
            ((int)response.StatusCode).ShouldBeOneOf(200, 409);

        responses.Count(r => r.IsSuccessStatusCode).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);

        var parents = await WithDbAsync(db => db.Positions.Where(p => p.Id == a || p.Id == b)
            .ToDictionaryAsync(p => p.Id, p => p.ParentPositionId));
        (parents[a] == b ^ parents[b] == a).ShouldBeTrue();
        (parents[a] is null || parents[b] is null).ShouldBeTrue();
    }

    [Test]
    public async Task ConcurrentRoleHierarchyMutation_CannotCreateCycle()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create", "AccessManagement.Role.Edit");
        var a = await CreateRoleViaApiAsync(session, "A");
        var b = await CreateRoleViaApiAsync(session, "B");

        var responses = await Task.WhenAll(
            UpdateRoleParentAsync(session, a, "A", b),
            UpdateRoleParentAsync(session, b, "B", a));

        foreach (var response in responses)
            ((int)response.StatusCode).ShouldBeOneOf(200, 409);

        responses.Count(r => r.IsSuccessStatusCode).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);

        var parents = await WithDbAsync(db => db.BusinessRoles.Where(r => r.Id == a || r.Id == b)
            .ToDictionaryAsync(r => r.Id, r => r.ParentRoleId));
        (parents[a] == b ^ parents[b] == a).ShouldBeTrue();
        (parents[a] is null || parents[b] is null).ShouldBeTrue();
    }

    [Test]
    public async Task RolePermissionRevoked_ThroughApi_NextRequestForbidden()
    {
        var admin = await CreateAuthorizedClientAsync("admin@test.com", _companyId,
            "AccessManagement.Role.Read", "AccessManagement.Role.Create", "AccessManagement.Role.Permissions.Manage");
        var superAdmin = await CreateRoleAsync(_companyId, _appId, "Super", "SUPER", RoleKind.CompanySuperAdmin);
        await WithDbAsync(async db =>
        {
            var membership = await db.UserCompanies.Include(uc => uc.Roles).SingleAsync(uc => uc.PrincipalId == admin.PrincipalId);
            membership.AddRole(superAdmin);
            await db.SaveChangesAsync(default);
        });

        var roleId = await CreateRoleViaApiAsync(admin, "READER");
        var emptySourceId = await CreateRoleViaApiAsync(admin, "EMPTY");
        var organizationId = await WithDbAsync(db => db.Resources.Where(r => r.ApplicationId == _appId && r.Code == "Organization").Select(r => r.Id).SingleAsync());

        (await admin.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{roleId}/permissions", new
        {
            Entries = new[]
            {
                new
                {
                    ResourceId = organizationId,
                    Actions = new[] { new { ActionCode = "Position.Read", Effect = AccessEffect.Allow, ScopeType = (string?)null, ScopeKeys = (string[]?)null } }
                }
            }
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var subject = await CreateRoleOnlyClientAsync("subject@test.com", roleId);
        (await subject.GetAsync("/api/v1/organization/positions/")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await admin.Client.PostAsJsonAsync($"/api/v1/access-control/roles/{roleId}/permissions/copy-from",
            new { SourceRoleId = emptySourceId, Mode = "REPLACE" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await subject.GetAsync("/api/v1/organization/positions/")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task CrossTenantSweep_PersonnelPositionsAndSignaturesStayIsolated()
    {
        var companyA = await CreateAuthorizedClientAsync("a@test.com", _companyId,
            "Organization.Personnel.Create", "Organization.Personnel.Read",
            "Organization.Position.Create", "Organization.Position.Read",
            "Organization.PersonnelPosition.Create", "Organization.PersonnelSignature.Create");
        var companyB = await CreateAuthorizedClientAsync("b@test.com", _otherCompanyId,
            "Organization.Personnel.Create", "Organization.Personnel.Read",
            "Organization.Position.Create", "Organization.Position.Read",
            "Organization.PersonnelPosition.Create", "Organization.PersonnelSignature.Create");

        var personnelA = await CreatePersonnelAsync(companyA, "1111111111");
        var personnelB = await CreatePersonnelAsync(companyB, "1111111111");
        var positionA = await CreatePositionAsync(companyA, "POSA");
        var positionB = await CreatePositionAsync(companyB, "POSB");
        (await AssignPrimaryAsync(companyA, personnelA, positionA)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await AssignPrimaryAsync(companyB, personnelB, positionB)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await companyA.Client.PostAsync($"/api/v1/organization/personnel/{personnelA}/signature", TestImages.SignatureUpload(TestImages.Png1x1)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await companyB.Client.PostAsync($"/api/v1/organization/personnel/{personnelB}/signature", TestImages.SignatureUpload(TestImages.Png1x1)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var listA = await companyA.Client.GetStringAsync("/api/v1/organization/personnel/?pageSize=100");
        var listB = await companyB.Client.GetStringAsync("/api/v1/organization/personnel/?pageSize=100");
        listA.ShouldContain(personnelA.ToString());
        listA.ShouldNotContain(personnelB.ToString());
        listB.ShouldContain(personnelB.ToString());
        listB.ShouldNotContain(personnelA.ToString());

        (await companyA.Client.GetAsync($"/api/v1/organization/personnel/{personnelB}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await companyB.Client.GetAsync($"/api/v1/organization/personnel/{personnelA}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await companyA.Client.GetAsync($"/api/v1/organization/positions/{positionB}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await companyB.Client.GetAsync($"/api/v1/organization/positions/{positionA}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await companyA.Client.PostAsync($"/api/v1/organization/personnel/{personnelB}/signature", TestImages.SignatureUpload(TestImages.Png1x1)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var positionsA = await companyA.Client.GetStringAsync("/api/v1/organization/positions/");
        var positionsB = await companyB.Client.GetStringAsync("/api/v1/organization/positions/");
        positionsA.ShouldContain("POSA");
        positionsA.ShouldNotContain("POSB");
        positionsB.ShouldContain("POSB");
        positionsB.ShouldNotContain("POSA");
    }

    private async Task<HttpClient> CreateRoleOnlyClientAsync(string email, Guid roleId)
    {
        const string password = "Test123!";
        var user = await CreateUserAsync(email, password);
        await WithDbAsync(async db =>
        {
            var qcApp = await db.Applications.SingleAsync(a => a.Code == "QC");
            var membership = new UserCompany(user.Id, _companyId, Guid.Empty);
            var principal = AuthPrincipal.ForUserCompany(membership.Id, _companyId, qcApp.Id);
            db.UserCompanies.Add(membership);
            db.Entry(membership).Property(uc => uc.PrincipalId).CurrentValue = principal.Id;
            db.AuthPrincipals.Add(principal);
            membership.AddRole(roleId);
            await db.SaveChangesAsync(default);
        });

        var token = await LoginAsync(email, password);
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Company-Id", _companyId.ToString());
        return client;
    }

    private async Task<Guid> CreatePersonnelAsync(AuthSession session, string nationalCode = "1234567890")
    {
        var response = await session.Client.PostAsJsonAsync("/api/v1/organization/personnel",
            new { NationalCode = nationalCode, FirstName = "Test", LastName = "Person", Gender = 1 });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private async Task<Guid> CreatePositionAsync(AuthSession session, string code)
    {
        var response = await session.Client.PostAsJsonAsync("/api/v1/organization/positions",
            new { CompanyId = session.Client.DefaultRequestHeaders.GetValues("X-Company-Id").Single(), Code = code, Title = code });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private async Task<Guid> CreateRoleViaApiAsync(AuthSession session, string code)
    {
        var response = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles", new
        {
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = code,
            Name = code,
            Kind = RoleKind.Standard
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private static Task<HttpResponseMessage> AssignPrimaryAsync(AuthSession session, Guid personnelId, Guid positionId)
        => session.Client.PostAsJsonAsync($"/api/v1/organization/personnel/{personnelId}/positions",
            new { PositionId = positionId, IsPrimary = true, EffectiveFrom = DateTime.UtcNow.AddDays(-1) });

    private static Task<HttpResponseMessage> UpdatePositionParentAsync(AuthSession session, Guid id, string code, Guid? parent)
        => session.Client.PutAsJsonAsync($"/api/v1/organization/positions/{id}",
            new { Code = code, Title = code, ParentPositionId = parent, Status = PositionStatus.Active });

    private static Task<HttpResponseMessage> UpdateRoleParentAsync(AuthSession session, Guid id, string code, Guid? parent)
        => session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{id}",
            new { Name = code, Code = code, Kind = RoleKind.Standard, ParentRoleId = parent, Status = RoleStatus.Active });

    private record IdResponse(Guid Id);
}
