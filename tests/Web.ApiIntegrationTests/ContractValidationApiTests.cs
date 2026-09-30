using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

/// <summary>
/// Malformed requests are rejected with 400 ValidationProblemDetails before any state changes.
/// </summary>
[TestFixture]
public class ContractValidationApiTests : ApiTestBase
{
    private static readonly string[] AllPermissions =
    [
        "Organization.Position.Read",
        "Organization.Position.Create",
        "Organization.Position.Edit",
        "Organization.Personnel.Read",
        "Organization.Personnel.Create",
        "Organization.Personnel.Edit",
        "Organization.PersonnelPosition.Create",
        "Organization.PersonnelPosition.Edit",
        "AccessManagement.Role.Read",
        "AccessManagement.Role.Create",
        "AccessManagement.Role.Permissions.Manage",
        "AccessManagement.Role.BulkAssign"
    ];

    private Guid _companyId;
    private Guid _appId;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _appId, _, _, _) = await SeedTestDataAsync();
    }

    [Test]
    public async Task CreatePosition_WithMissingCode_Returns400AndCreatesNothing()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, AllPermissions);

        var response = await session.Client.PostAsJsonAsync("/api/v1/organization/positions",
            new { CompanyId = _companyId, Code = "", Title = "Untitled" });

        await ShouldBeValidationErrorAsync(response, "Code");
        (await WithDbAsync(db => db.Positions.AnyAsync(p => p.Title == "Untitled"))).ShouldBeFalse();
    }

    [Test]
    public async Task CreatePosition_WithOverlongCodeOrUnknownStatus_Returns400()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, AllPermissions);

        var tooLong = await session.Client.PostAsJsonAsync("/api/v1/organization/positions",
            new { CompanyId = _companyId, Code = new string('C', 51), Title = "T" });
        await ShouldBeValidationErrorAsync(tooLong, "Code");

        var badStatus = await session.Client.PostAsJsonAsync("/api/v1/organization/positions",
            new { CompanyId = _companyId, Code = "POS", Title = "T", Status = 99 });
        await ShouldBeValidationErrorAsync(badStatus, "Status");
    }

    [Test]
    public async Task CreatePosition_Description_IsPersisted()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, AllPermissions);

        var response = await session.Client.PostAsJsonAsync("/api/v1/organization/positions",
            new { CompanyId = _companyId, Code = "DESC", Title = "Described", Description = "Kept" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await WithDbAsync(db => db.Positions.Where(p => p.Code == "DESC").Select(p => p.Description).SingleAsync()))
            .ShouldBe("Kept");
    }

    [TestCase("12345")]
    [TestCase("12345678901")]
    [TestCase("12345abcde")]
    public async Task CreatePersonnel_WithMalformedNationalCode_Returns400AndCreatesNothing(string nationalCode)
    {
        var session = await CreateAuthorizedClientAsync(_companyId, AllPermissions);

        var response = await session.Client.PostAsJsonAsync("/api/v1/organization/personnel",
            new { NationalCode = nationalCode, FirstName = "Bad", LastName = "Code", Gender = 1 });

        await ShouldBeValidationErrorAsync(response, "NationalCode");
        (await WithDbAsync(db => db.Personnel.AnyAsync(p => p.NationalCode == nationalCode))).ShouldBeFalse();
    }

    [Test]
    public async Task CreatePersonnel_WithUnknownGender_Returns400()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, AllPermissions);

        var response = await session.Client.PostAsJsonAsync("/api/v1/organization/personnel",
            new { NationalCode = "1234567890", FirstName = "A", LastName = "B", Gender = 99 });

        await ShouldBeValidationErrorAsync(response, "Gender");
    }

    [Test]
    public async Task UpdatePersonnel_WithMalformedNationalCode_Returns400AndKeepsOriginal()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, AllPermissions);
        var personnelId = await CreatePersonnelAsync(session.Client, "1234567890");

        var response = await session.Client.PutAsJsonAsync($"/api/v1/organization/personnel/{personnelId}",
            new { FirstName = "A", LastName = "B", NationalCode = "12AB", Gender = 1 });

        await ShouldBeValidationErrorAsync(response, "NationalCode");
        (await WithDbAsync(db => db.Personnel.Where(p => p.Id == personnelId).Select(p => p.NationalCode).SingleAsync()))
            .ShouldBe("1234567890");
    }

    [Test]
    public async Task AssignPosition_WithEffectiveToNotAfterEffectiveFrom_Returns400AndCreatesNothing()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, AllPermissions);
        var personnelId = await CreatePersonnelAsync(session.Client, "1234567890");
        var positionId = await CreatePositionAsync(session.Client, "RANGE");
        var from = DateTime.UtcNow.AddDays(1);

        var response = await session.Client.PostAsJsonAsync($"/api/v1/organization/personnel/{personnelId}/positions",
            new { PositionId = positionId, IsPrimary = false, EffectiveFrom = from, EffectiveTo = from });

        await ShouldBeValidationErrorAsync(response, "EffectiveTo");
        (await WithDbAsync(db => db.PersonnelPositions.AnyAsync(pp => pp.PersonnelId == personnelId))).ShouldBeFalse();
    }

    [Test]
    public async Task UpdatePositionAssignment_WithEffectiveToBeforeEffectiveFrom_Returns400()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, AllPermissions);
        var personnelId = await CreatePersonnelAsync(session.Client, "1234567890");
        var positionId = await CreatePositionAsync(session.Client, "RANGE");
        var assign = await session.Client.PostAsJsonAsync($"/api/v1/organization/personnel/{personnelId}/positions",
            new { PositionId = positionId, IsPrimary = false, EffectiveFrom = DateTime.UtcNow.AddDays(-1), EffectiveTo = (DateTime?)null });
        assign.StatusCode.ShouldBe(HttpStatusCode.Created);
        var assignmentId = (await assign.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var response = await session.Client.PutAsJsonAsync($"/api/v1/organization/personnel/{personnelId}/positions/{assignmentId}",
            new { IsPrimary = false, EffectiveFrom = DateTime.UtcNow.AddDays(10), EffectiveTo = DateTime.UtcNow.AddDays(5), Status = 0 });

        await ShouldBeValidationErrorAsync(response, "EffectiveTo");
    }

    [Test]
    public async Task CreateRole_WithMissingNameOrUnknownKind_Returns400AndCreatesNothing()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, AllPermissions);

        var noName = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles",
            new { CompanyId = _companyId, ApplicationId = _appId, Code = "NONAME", Name = "" });
        await ShouldBeValidationErrorAsync(noName, "Name");

        var badKind = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles",
            new { CompanyId = _companyId, ApplicationId = _appId, Code = "BADKIND", Name = "Bad", Kind = 99 });
        await ShouldBeValidationErrorAsync(badKind, "Kind");

        (await WithDbAsync(db => db.BusinessRoles.AnyAsync(r => r.Code == "NONAME" || r.Code == "BADKIND"))).ShouldBeFalse();
    }

    [Test]
    public async Task CopyRolePermissions_WithUnknownMode_Returns400AndCopiesNothing()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, AllPermissions);
        var sourceId = await CreateRoleAsync(_companyId, _appId, "Source", "SOURCE");
        var targetId = await CreateRoleAsync(_companyId, _appId, "Target", "TARGET");

        var response = await session.Client.PostAsJsonAsync($"/api/v1/access-control/roles/{targetId}/permissions/copy-from",
            new { SourceRoleId = sourceId, Mode = "MERGE" });

        await ShouldBeValidationErrorAsync(response, "Mode");
    }

    [Test]
    public async Task BulkAssign_WithNoUserCompanies_Returns400()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, AllPermissions);
        var roleId = await CreateRoleAsync(_companyId, _appId, "Clerk", "CLERK");

        var response = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles/bulk-assign",
            new { RoleId = roleId, UserCompanyIds = Array.Empty<Guid>() });

        await ShouldBeValidationErrorAsync(response, "UserCompanyIds");
    }

    [TestCase("/api/v1/organization/positions", "page=0", "Page")]
    [TestCase("/api/v1/organization/positions", "pageSize=0", "PageSize")]
    [TestCase("/api/v1/organization/positions", "pageSize=101", "PageSize")]
    [TestCase("/api/v1/organization/personnel", "page=-1", "Page")]
    [TestCase("/api/v1/organization/personnel", "pageSize=101", "PageSize")]
    [TestCase("/api/v1/access-control/roles", "page=0", "Page")]
    [TestCase("/api/v1/access-control/roles", "pageSize=101", "PageSize")]
    public async Task List_WithOutOfRangePagination_Returns400(string path, string query, string field)
    {
        var session = await CreateAuthorizedClientAsync(_companyId, AllPermissions);

        var response = await session.Client.GetAsync($"{path}?{query}");

        await ShouldBeValidationErrorAsync(response, field);
    }

    [TestCase("/api/v1/organization/positions")]
    [TestCase("/api/v1/organization/personnel")]
    [TestCase("/api/v1/access-control/roles")]
    public async Task List_WithMaximumPageSize_ReturnsOk(string path)
    {
        var session = await CreateAuthorizedClientAsync(_companyId, AllPermissions);

        var response = await session.Client.GetAsync($"{path}?page=1&pageSize=100");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task ShouldBeValidationErrorAsync(HttpResponseMessage response, string field)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errorKeys = body.RootElement.GetProperty("errors").EnumerateObject().Select(e => e.Name).ToList();
        errorKeys.ShouldContain(k => string.Equals(k, field, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Guid> CreatePersonnelAsync(HttpClient client, string nationalCode)
    {
        var response = await client.PostAsJsonAsync("/api/v1/organization/personnel",
            new { NationalCode = nationalCode, FirstName = "Test", LastName = "Person", Gender = 1 });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private async Task<Guid> CreatePositionAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/v1/organization/positions",
            new { CompanyId = _companyId, Code = code, Title = code });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private record IdResponse(Guid Id);
}
