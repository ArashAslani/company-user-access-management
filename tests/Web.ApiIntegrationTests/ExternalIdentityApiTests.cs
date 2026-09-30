using CompanyAccessManagement.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System.Net;
using System.Net.Http.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

/// <summary>
/// ERP/HR identities through the existing create/update endpoints. There are no upsert endpoints: a sync client
/// resolves the external identity and then creates or updates.
/// </summary>
[TestFixture]
public class ExternalIdentityApiTests : ApiTestBase
{
    private Guid _companyId;
    private AuthSession _session = null!;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _, _, _, _) = await SeedTestDataAsync();
        _session = await CreateAuthorizedClientAsync(_companyId,
            "Organization.Position.Read", "Organization.Position.Create", "Organization.Position.Edit",
            "Organization.Personnel.Read", "Organization.Personnel.Create", "Organization.Personnel.Edit",
            "Organization.PersonnelPosition.Create");
    }

    [Test]
    public async Task RepeatedExternalSync_DoesNotCreateDuplicate()
    {
        await SyncPositionAsync("POS-9", "Welder");
        await SyncPersonnelAsync("EMP-9", "Sara");

        await SyncPositionAsync("POS-9", "Senior Welder");
        await SyncPersonnelAsync("EMP-9", "Sarah");

        var positions = await WithDbAsync(db => db.Positions.Where(p => p.CompanyId == _companyId && p.ExternalId == "POS-9").ToListAsync());
        positions.Count.ShouldBe(1);
        positions[0].Title.ShouldBe("Senior Welder");
        positions[0].ExternalSource.ShouldBe("SAP");

        var personnel = await WithDbAsync(db => db.Personnel.Where(p => p.CompanyId == _companyId && p.ExternalId == "EMP-9").ToListAsync());
        personnel.Count.ShouldBe(1);
        personnel[0].FirstName.ShouldBe("Sarah");
    }

    [Test]
    public async Task CreatePosition_DuplicateExternalIdentity_Returns409()
    {
        (await CreatePositionAsync("P1", "sap", "POS-1")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await CreatePositionAsync("P2", "SAP", "POS-1");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("EXTERNAL_IDENTITY_DUPLICATE");
        (await WithDbAsync(db => db.Positions.CountAsync(p => p.ExternalId == "POS-1"))).ShouldBe(1);
    }

    [Test]
    public async Task UpdatePersonnel_ExternalIdentityOfAnotherPersonnel_Returns409()
    {
        await CreatePersonnelAsync("1111111111", "HRIS", "EMP-1");
        var second = await CreatePersonnelAsync("2222222222", null, null);

        var response = await _session.Client.PutAsJsonAsync($"/api/v1/organization/personnel/{second}",
            new { FirstName = "Test", LastName = "Person", NationalCode = "2222222222", Gender = 1, ExternalSource = "HRIS", ExternalId = "EMP-1" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("EXTERNAL_IDENTITY_DUPLICATE");
        (await WithDbAsync(db => db.Personnel.Where(p => p.Id == second).Select(p => p.ExternalId).SingleAsync())).ShouldBeNull();
    }

    [Test]
    public async Task CreatePersonnel_IncompleteExternalIdentity_Returns400()
    {
        var response = await _session.Client.PostAsJsonAsync("/api/v1/organization/personnel",
            new { NationalCode = "3333333333", FirstName = "Test", LastName = "Person", Gender = 1, ExternalSource = "HRIS" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("ExternalId");
        (await WithDbAsync(db => db.Personnel.AnyAsync(p => p.NationalCode == "3333333333"))).ShouldBeFalse();
    }

    [Test]
    public async Task AssignPosition_ExternalIdentity_IsPersistedAndResolvable()
    {
        var personnelId = await CreatePersonnelAsync("4444444444", null, null);
        var positionResponse = await CreatePositionAsync("P4", null, null);
        var positionId = (await positionResponse.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var response = await _session.Client.PostAsJsonAsync($"/api/v1/organization/personnel/{personnelId}/positions",
            new { PositionId = positionId, IsPrimary = true, EffectiveFrom = DateTime.UtcNow.AddDays(-1), ExternalSource = "hris", ExternalId = "ASG-4" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var assignmentId = await ResolveAsync(r => r.ResolvePersonnelPositionAsync(personnelId, "HRIS", "ASG-4"));
        assignmentId.ShouldNotBeNull();
        (await WithDbAsync(db => db.PersonnelPositions.Where(pp => pp.Id == assignmentId).Select(pp => pp.PositionId).SingleAsync())).ShouldBe(positionId);
    }

    private async Task SyncPositionAsync(string externalId, string title)
    {
        var existing = await ResolveAsync(r => r.ResolvePositionAsync(_companyId, "sap", externalId));
        var body = new { CompanyId = _companyId, Code = externalId, Title = title, Status = 0, ExternalSource = "sap", ExternalId = externalId };

        var response = existing is Guid id
            ? await _session.Client.PutAsJsonAsync($"/api/v1/organization/positions/{id}", body)
            : await _session.Client.PostAsJsonAsync("/api/v1/organization/positions", body);

        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
    }

    private async Task SyncPersonnelAsync(string externalId, string firstName)
    {
        var existing = await ResolveAsync(r => r.ResolvePersonnelAsync(_companyId, "SAP", externalId));
        var body = new { NationalCode = "5555555555", FirstName = firstName, LastName = "Person", Gender = 1, ExternalSource = "SAP", ExternalId = externalId };

        var response = existing is Guid id
            ? await _session.Client.PutAsJsonAsync($"/api/v1/organization/personnel/{id}", body)
            : await _session.Client.PostAsJsonAsync("/api/v1/organization/personnel", body);

        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid?> ResolveAsync(Func<IExternalOrganizationResolver, Task<Guid?>> resolve)
    {
        using var scope = Factory.Services.CreateScope();
        return await resolve(scope.ServiceProvider.GetRequiredService<IExternalOrganizationResolver>());
    }

    private Task<HttpResponseMessage> CreatePositionAsync(string code, string? source, string? externalId)
        => _session.Client.PostAsJsonAsync("/api/v1/organization/positions",
            new { CompanyId = _companyId, Code = code, Title = code, ExternalSource = source, ExternalId = externalId });

    private async Task<Guid> CreatePersonnelAsync(string nationalCode, string? source, string? externalId)
    {
        var response = await _session.Client.PostAsJsonAsync("/api/v1/organization/personnel",
            new { NationalCode = nationalCode, FirstName = "Test", LastName = "Person", Gender = 1, ExternalSource = source, ExternalId = externalId });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private record IdResponse(Guid Id);
}
