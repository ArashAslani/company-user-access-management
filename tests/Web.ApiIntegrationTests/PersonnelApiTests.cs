using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Models;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Organization;
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
using Shouldly;
using System.Net;
using System.Net.Http.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

[TestFixture]
public class PersonnelApiTests : ApiTestBase
{
    private Guid _companyId;
    private Guid _appId;
    private Guid _productsReadPermId;
    private Guid _productsEditPermId;
    private Guid _productsDeletePermId;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _appId, _productsReadPermId, _productsEditPermId, _productsDeletePermId) = await SeedTestDataAsync();
    }

    [Test]
    public async Task GetPersonnel_WithPermission_ReturnsOk()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Read", "Organization.Personnel.Create", "Organization.Position.Read", "Organization.Position.Create", "Organization.PersonnelPosition.Create");

        // Create personnel via API
        var createCommand = new
        {
            CompanyId = _companyId,
            NationalCode = "1234567890",
            FirstName = "John",
            LastName = "Doe",
            Gender = 1,
            PersonnelCode = "P001",
            PhoneNumber = "555-1234"
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/personnel", createCommand);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreatePersonnelResponse>();
        var personnelId = createResult!.Id;

        // Create position via API
        var positionCommand = new
        {
            CompanyId = _companyId,
            Code = "TESTPOS",
            Title = "Test Position",
            Description = "Test Description",
            Kind = "Organizational",
            HoldingId = _companyId
        };

        var positionResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/positions", positionCommand);
        positionResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var positionResult = await positionResponse.Content.ReadFromJsonAsync<CreatePositionResponse>();
        var positionId = positionResult!.Id;

        // Assign position via API
        var assignCommand = new
        {
            PersonnelId = personnelId,
            PositionId = positionId,
            IsPrimary = true,
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            EffectiveTo = (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(30)
        };

        var assignResponse = await session.Client.PostAsJsonAsync($"/api/v1/organization/personnel/{personnelId}/positions", assignCommand);
        assignResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Verify via API
        var url = $"/api/v1/organization/personnel/?companyId={_companyId}";
        var absoluteUrl = new Uri(session.Client.BaseAddress!, url);
        
        var response = await session.Client.GetAsync(absoluteUrl);
        
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PaginatedList<PersonnelDto>>();
        result.ShouldNotBeNull();
        result.Items.ShouldNotBeNull();
        result.Items.Count.ShouldBe(1);
    }

    [Test]
    public async Task GetPersonnel_WithoutPermission_ReturnsForbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId); // No permissions

        var response = await session.Client.GetAsync($"/api/v1/organization/personnel/?companyId={_companyId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task CreatePersonnel_WithValidData_ReturnsCreated()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Create");

        var command = new
        {
            CompanyId = _companyId,
            NationalCode = "1234567890",
            FirstName = "Jane",
            LastName = "Smith",
            Gender = 1,
            PersonnelCode = "P002",
            PhoneNumber = "555-5678"
        };

        var response = await session.Client.PostAsJsonAsync("/api/v1/organization/personnel", command);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var result = await response.Content.ReadFromJsonAsync<CreatePersonnelResponse>();
        result.ShouldNotBeNull();
        result.Id.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task GetPersonnelDetail_ById_ReturnsOk()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Read", "Organization.Personnel.Create");

        var createCommand = new
        {
            CompanyId = _companyId,
            NationalCode = "1234567890",
            FirstName = "John",
            LastName = "Doe",
            Gender = 1,
            PersonnelCode = "P001",
            PhoneNumber = "555-1234"
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/personnel", createCommand);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreatePersonnelResponse>();
        var personnelId = createResult!.Id;

        var response = await session.Client.GetAsync($"/api/v1/organization/personnel/{personnelId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PersonnelDetailDto>();
        result.ShouldNotBeNull();
        result.FirstName.ShouldBe("John");
    }

    [Test]
    public async Task DeletePersonnel_WithNoAssignments_ReturnsNoContent()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Read", "Organization.Personnel.Delete", "Organization.Personnel.Create");

        var createCommand = new
        {
            CompanyId = _companyId,
            NationalCode = "1234567890",
            FirstName = "John",
            LastName = "Doe",
            Gender = 1,
            PersonnelCode = "P001",
            PhoneNumber = "555-1234"
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/personnel", createCommand);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreatePersonnelResponse>();
        var personnelId = createResult!.Id;

        var response = await session.Client.DeleteAsync($"/api/v1/organization/personnel/{personnelId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task AssignPosition_WithValidData_ReturnsOk()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Create", "Organization.Position.Create", "Organization.PersonnelPosition.Create");

        var createCommand = new
        {
            CompanyId = _companyId,
            NationalCode = "1234567890",
            FirstName = "John",
            LastName = "Doe",
            Gender = 1,
            PersonnelCode = "P001",
            PhoneNumber = "555-1234"
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/personnel", createCommand);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreatePersonnelResponse>();
        var personnelId = createResult!.Id;

        var positionCommand = new
        {
            CompanyId = _companyId,
            Code = "TESTPOS",
            Title = "Test Position",
            Description = "Test Description",
            Kind = "Organizational",
            HoldingId = _companyId
        };

        var positionResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/positions", positionCommand);
        positionResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var positionResult = await positionResponse.Content.ReadFromJsonAsync<CreatePositionResponse>();
        var positionId = positionResult!.Id;

        var assignCommand = new
        {
            PersonnelId = personnelId,
            PositionId = positionId,
            IsPrimary = true,
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            EffectiveTo = (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(30)
        };

        var assignResponse = await session.Client.PostAsJsonAsync($"/api/v1/organization/personnel/{personnelId}/positions", assignCommand);
        assignResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task RemovePositionAssignment_ReturnsNoContent()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Create", "Organization.Position.Create", "Organization.PersonnelPosition.Create", "Organization.PersonnelPosition.Delete");

        var createCommand = new
        {
            CompanyId = _companyId,
            NationalCode = "1234567890",
            FirstName = "John",
            LastName = "Doe",
            Gender = 1,
            PersonnelCode = "P001",
            PhoneNumber = "555-1234"
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/personnel", createCommand);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreatePersonnelResponse>();
        var personnelId = createResult!.Id;

        var positionCommand = new
        {
            CompanyId = _companyId,
            Code = "TESTPOS",
            Title = "Test Position",
            Description = "Test Description",
            Kind = "Organizational",
            HoldingId = _companyId
        };

        var positionResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/positions", positionCommand);
        positionResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var positionResult = await positionResponse.Content.ReadFromJsonAsync<CreatePositionResponse>();
        var positionId = positionResult!.Id;

        var assignCommand = new
        {
            PersonnelId = personnelId,
            PositionId = positionId,
            IsPrimary = true,
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            EffectiveTo = (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(30)
        };

        var assignResponse = await session.Client.PostAsJsonAsync($"/api/v1/organization/personnel/{personnelId}/positions", assignCommand);
        assignResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await session.Client.DeleteAsync($"/api/v1/organization/personnel/{personnelId}/positions/{positionId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task UpdatePersonnel_WithValidData_ReturnsOk()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Read", "Organization.Personnel.Edit", "Organization.Personnel.Create");

        var createCommand = new
        {
            CompanyId = _companyId,
            NationalCode = "1234567890",
            FirstName = "John",
            LastName = "Doe",
            Gender = 1,
            PersonnelCode = "P001",
            PhoneNumber = "555-1234"
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/personnel", createCommand);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreatePersonnelResponse>();
        var personnelId = createResult!.Id;

        var command = new
        {
            Id = personnelId,
            CompanyId = _companyId,
            NationalCode = "1234567890",
            FirstName = "Updated",
            LastName = "Name",
            Gender = 1,
            PersonnelCode = "P001",
            PhoneNumber = "555-9999"
        };

        var response = await session.Client.PutAsJsonAsync($"/api/v1/organization/personnel/{personnelId}", command);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        // Verify via fresh scope
        await WithDbAsync(async db =>
        {
            var updated = await db.Personnel
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == personnelId);
            updated!.FirstName.ShouldBe("Updated");
        });
    }

    [Test]
    public async Task UploadSignature_WithValidFile_ReturnsCreated()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.PersonnelSignature.Create", "Organization.Personnel.Create");

        var createCommand = new
        {
            CompanyId = _companyId,
            NationalCode = "1234567890",
            FirstName = "John",
            LastName = "Doe",
            Gender = 1,
            PersonnelCode = "P001",
            PhoneNumber = "555-1234"
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/personnel", createCommand);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreatePersonnelResponse>();
        var personnelId = createResult!.Id;

        var fileContent = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        var content = new MultipartFormDataContent
        {
            { fileContent, "file", "signature.png" }
        };

        var response = await session.Client.PostAsync($"/api/v1/organization/personnel/{personnelId}/signature", content);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private record CreatePersonnelResponse(Guid Id);
}