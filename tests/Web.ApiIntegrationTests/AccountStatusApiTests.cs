using CompanyAccessManagement.Domain.Organization;
using CompanyAccessManagement.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;
using System.Net;
using System.Net.Http.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

/// <summary>
/// Inactive or deleted accounts cannot log in or refresh, tokens issued before deactivation get no workspace, and a
/// personnel has at most one live operational account.
/// </summary>
[TestFixture]
public class AccountStatusApiTests : ApiTestBase
{
    private const string Password = "Test123!";
    private Guid _companyId;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _, _, _, _) = await SeedTestDataAsync();
    }

    [Test]
    public async Task InactiveAccount_Login_Unauthorized()
    {
        await CreateUserAsync("inactive@test.com", Password, isActive: false);

        var response = await Client.PostAsJsonAsync("/login", new { email = "inactive@test.com", password = Password });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task DeletedAccount_Login_Unauthorized()
    {
        var user = await CreateUserAsync("deleted@test.com", Password);
        await UpdateUserAsync(user.Id, u => { u.IsDeleted = true; u.DeletedAt = DateTime.UtcNow; });

        var response = await Client.PostAsJsonAsync("/login", new { email = "deleted@test.com", password = Password });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task AccountDeactivatedAfterLogin_BusinessEndpointForbidden_RefreshUnauthorized()
    {
        var session = await CreateAuthorizedClientAsync("member@test.com", _companyId, "Organization.Position.Read");
        var login = await (await Client.PostAsJsonAsync("/login", new { email = "member@test.com", password = Password }))
            .Content.ReadFromJsonAsync<TokenResponse>();

        (await session.Client.GetAsync("/api/v1/organization/positions")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client.PostAsJsonAsync("/refresh", new { refreshToken = login!.RefreshToken })).StatusCode.ShouldBe(HttpStatusCode.OK);

        await UpdateUserAsync(session.UserId, u => u.IsActive = false);

        (await session.Client.GetAsync("/api/v1/organization/positions")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Client.PostAsJsonAsync("/refresh", new { refreshToken = login.RefreshToken })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task DeletedAccount_ExistingToken_BusinessEndpointForbidden()
    {
        var session = await CreateAuthorizedClientAsync("member@test.com", _companyId, "Organization.Position.Read");

        await UpdateUserAsync(session.UserId, u => { u.IsDeleted = true; u.DeletedAt = DateTime.UtcNow; });

        (await session.Client.GetAsync("/api/v1/organization/positions")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Personnel_TwoLiveAccounts_Rejected_DeletedPlusLive_Allowed()
    {
        var personnelId = await WithDbAsync(async db =>
        {
            var personnel = new Personnel(_companyId, "7000000001", "Linked", "Person", Gender.Male);
            db.Personnel.Add(personnel);
            await db.SaveChangesAsync(default);
            return personnel.Id;
        });
        var first = await CreateUserAsync("first@test.com", Password);
        var second = await CreateUserAsync("second@test.com", Password);

        await UpdateUserAsync(first.Id, u => u.PersonnelId = personnelId);
        await Should.ThrowAsync<DbUpdateException>(() => UpdateUserAsync(second.Id, u => u.PersonnelId = personnelId));

        await UpdateUserAsync(first.Id, u => { u.IsDeleted = true; u.DeletedAt = DateTime.UtcNow; });
        await UpdateUserAsync(second.Id, u => u.PersonnelId = personnelId);

        var linked = await WithDbAsync(db => db.Users.AsNoTracking().Where(u => u.PersonnelId == personnelId).Select(u => new { u.Id, u.IsDeleted }).ToListAsync());
        linked.Count.ShouldBe(2);
        linked.Single(u => !u.IsDeleted).Id.ShouldBe(second.Id);
    }

    private Task UpdateUserAsync(Guid userId, Action<ApplicationUser> change)
        => WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            change(user);
            await db.SaveChangesAsync(default);
        });

    private record TokenResponse(string AccessToken, string RefreshToken);
}
