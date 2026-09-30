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
            Description = "Test Description"
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
        assignResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

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
    public async Task AssignPosition_WithValidData_ReturnsCreatedWithAssignmentId()
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
            Description = "Test Description"
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
        assignResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var result = await assignResponse.Content.ReadFromJsonAsync<IdResponse>();
        result!.Id.ShouldNotBe(Guid.Empty);
        await WithDbAsync(async db =>
            (await db.PersonnelPositions.AsNoTracking().SingleAsync(pp => pp.Id == result.Id)).PersonnelId.ShouldBe(personnelId));
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
            Description = "Test Description"
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
        assignResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var assignment = await assignResponse.Content.ReadFromJsonAsync<IdResponse>();
        var response = await session.Client.DeleteAsync($"/api/v1/organization/personnel/{personnelId}/positions/{assignment!.Id}");

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
    public async Task UpdatePersonnel_NationalCode_UpdatesPersistedValue()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Create", "Organization.Personnel.Read", "Organization.Personnel.Edit");
        var personnelId = await CreatePersonnelAsync(session.Client, "1234567890");

        var response = await session.Client.PutAsJsonAsync($"/api/v1/organization/personnel/{personnelId}",
            new { NationalCode = "0987654321", FirstName = "Test", LastName = "Person", Gender = 1 });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await WithDbAsync(db => db.Personnel.Where(p => p.Id == personnelId).Select(p => p.NationalCode).SingleAsync()))
            .ShouldBe("0987654321");
    }

    [Test]
    public async Task UpdatePersonnel_SameNationalCode_RemainsAllowed()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Create", "Organization.Personnel.Read", "Organization.Personnel.Edit");
        var personnelId = await CreatePersonnelAsync(session.Client, "1234567890");

        var response = await session.Client.PutAsJsonAsync($"/api/v1/organization/personnel/{personnelId}",
            new { NationalCode = "1234567890", FirstName = "Renamed", LastName = "Person", Gender = 1 });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await WithDbAsync(db => db.Personnel.Where(p => p.Id == personnelId).Select(p => p.NationalCode).SingleAsync()))
            .ShouldBe("1234567890");
    }

    [Test]
    public async Task UpdatePersonnel_DuplicateNationalCodeInSameCompany_ReturnsConflict()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Create", "Organization.Personnel.Read", "Organization.Personnel.Edit");
        await CreatePersonnelAsync(session.Client, "1111111111");
        var personnelId = await CreatePersonnelAsync(session.Client, "2222222222");

        var response = await session.Client.PutAsJsonAsync($"/api/v1/organization/personnel/{personnelId}",
            new { NationalCode = "1111111111", FirstName = "Test", LastName = "Person", Gender = 1 });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await WithDbAsync(db => db.Personnel.Where(p => p.Id == personnelId).Select(p => p.NationalCode).SingleAsync()))
            .ShouldBe("2222222222");
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

        var fileContent = new ByteArrayContent(TestImages.Png1x1);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        var content = new MultipartFormDataContent
        {
            { fileContent, "file", "signature.png" }
        };

        var response = await session.Client.PostAsync($"/api/v1/organization/personnel/{personnelId}/signature", content);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task UpdatePositionAssignment_TargetsTheGivenAssignmentId()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Create", "Organization.Position.Create", "Organization.PersonnelPosition.Create", "Organization.PersonnelPosition.Edit");
        var personnelId = await CreatePersonnelAsync(session.Client, "1111111111");
        var positionId = await CreatePositionAsync(session.Client, "UPDPOS");

        var first = await AssignAsync(session.Client, personnelId, positionId, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(10));
        var second = await AssignAsync(session.Client, personnelId, positionId, DateTime.UtcNow.AddDays(20), DateTime.UtcNow.AddDays(30));

        var newTo = DateTime.UtcNow.AddDays(45);
        var response = await session.Client.PutAsJsonAsync(
            $"/api/v1/organization/personnel/{personnelId}/positions/{second}",
            new { IsPrimary = false, EffectiveFrom = DateTime.UtcNow.AddDays(20), EffectiveTo = newTo, Status = 0 });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await WithDbAsync(async db =>
        {
            var rows = await db.PersonnelPositions.AsNoTracking().Where(pp => pp.PersonnelId == personnelId).ToListAsync();
            rows.Single(r => r.Id == second).EffectiveTo!.Value.Date.ShouldBe(newTo.Date);
            rows.Single(r => r.Id == first).EffectiveTo!.Value.Date.ShouldBe(DateTime.UtcNow.AddDays(10).Date);
        });
    }

    [Test]
    public async Task UpdatePositionAssignment_OverlappingSibling_ReturnsConflictWithCode()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Create", "Organization.Position.Create", "Organization.PersonnelPosition.Create", "Organization.PersonnelPosition.Edit");
        var personnelId = await CreatePersonnelAsync(session.Client, "2222222222");
        var positionId = await CreatePositionAsync(session.Client, "OVLPOS");

        await AssignAsync(session.Client, personnelId, positionId, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(10));
        var second = await AssignAsync(session.Client, personnelId, positionId, DateTime.UtcNow.AddDays(20), DateTime.UtcNow.AddDays(30));

        var response = await session.Client.PutAsJsonAsync(
            $"/api/v1/organization/personnel/{personnelId}/positions/{second}",
            new { IsPrimary = false, EffectiveFrom = DateTime.UtcNow.AddDays(5), EffectiveTo = DateTime.UtcNow.AddDays(30), Status = 0 });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("ASSIGNMENT_OVERLAP");
    }

    [Test]
    public async Task AssignPosition_Overlapping_ReturnsConflictWithCode()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Create", "Organization.Position.Create", "Organization.PersonnelPosition.Create");
        var personnelId = await CreatePersonnelAsync(session.Client, "3333333333");
        var positionId = await CreatePositionAsync(session.Client, "DUPPOS");

        await AssignAsync(session.Client, personnelId, positionId, DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(5));

        var response = await session.Client.PostAsJsonAsync(
            $"/api/v1/organization/personnel/{personnelId}/positions",
            new { PositionId = positionId, IsPrimary = false, EffectiveFrom = DateTime.UtcNow.AddDays(-1), EffectiveTo = (DateTime?)null });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("ASSIGNMENT_OVERLAP");
    }

    [Test]
    public async Task RemovePositionAssignment_BelongingToOtherPersonnel_ReturnsNotFound()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Create", "Organization.Position.Create", "Organization.PersonnelPosition.Create", "Organization.PersonnelPosition.Delete");
        var owner = await CreatePersonnelAsync(session.Client, "4444444444");
        var other = await CreatePersonnelAsync(session.Client, "5555555555");
        var positionId = await CreatePositionAsync(session.Client, "OWNPOS");

        var assignmentId = await AssignAsync(session.Client, owner, positionId, DateTime.UtcNow.AddDays(-1), null);

        var response = await session.Client.DeleteAsync($"/api/v1/organization/personnel/{other}/positions/{assignmentId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await WithDbAsync(async db =>
            (await db.PersonnelPositions.AsNoTracking().SingleAsync(pp => pp.Id == assignmentId)).Status.ShouldBe(PersonnelPositionStatus.Active));
    }

    [Test]
    public async Task RemovePositionAssignment_EndedAssignment_ReturnsSealedConflict()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Create", "Organization.Position.Create", "Organization.PersonnelPosition.Create", "Organization.PersonnelPosition.Delete");
        var personnelId = await CreatePersonnelAsync(session.Client, "6666666666");
        var positionId = await CreatePositionAsync(session.Client, "OLDPOS");

        var assignmentId = await AssignAsync(session.Client, personnelId, positionId, DateTime.UtcNow.AddDays(-20), DateTime.UtcNow.AddDays(-10));

        var response = await session.Client.DeleteAsync($"/api/v1/organization/personnel/{personnelId}/positions/{assignmentId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("SEALED_RECORD");
    }

    [Test]
    public async Task UploadSignature_Twice_KeepsOneCurrentVersion()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.PersonnelSignature.Create", "Organization.Personnel.Create");
        var personnelId = await CreatePersonnelAsync(session.Client, "7777777777");

        var first = await UploadPngAsync(session.Client, personnelId);
        var second = await UploadPngAsync(session.Client, personnelId);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        var secondId = (await second.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        await WithDbAsync(async db =>
        {
            var signatures = await db.PersonnelSignatures.AsNoTracking().Where(s => s.PersonnelId == personnelId).ToListAsync();
            signatures.Select(s => s.Version).OrderBy(v => v).ShouldBe(new[] { 1, 2 });
            signatures.Single(s => s.IsCurrent).Id.ShouldBe(secondId);
            signatures.ShouldAllBe(s => s.CreatedByUserId == session.UserId);
        });
    }

    [Test]
    public async Task UploadSignature_ContentNotMatchingDeclaredType_ReturnsConflict()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.PersonnelSignature.Create", "Organization.Personnel.Create");
        var personnelId = await CreatePersonnelAsync(session.Client, "8888888888");

        var fileContent = new ByteArrayContent("not an image"u8.ToArray());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        var content = new MultipartFormDataContent { { fileContent, "file", "signature.png" } };

        var response = await session.Client.PostAsync($"/api/v1/organization/personnel/{personnelId}/signature", content);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("SIGNATURE_TYPE_NOT_ALLOWED");
    }

    private static async Task<HttpResponseMessage> UploadPngAsync(HttpClient client, Guid personnelId)
    {
        var fileContent = new ByteArrayContent(TestImages.Png1x1);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        var content = new MultipartFormDataContent { { fileContent, "file", "signature.png" } };
        return await client.PostAsync($"/api/v1/organization/personnel/{personnelId}/signature", content);
    }
    [Test]
    public async Task Primary_SameCompany_Overlap_Denied()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Create", "Organization.Position.Create", "Organization.PersonnelPosition.Create");
        var personnelId = await CreatePersonnelAsync(session.Client, "1212121212");
        var positionA = await CreatePositionAsync(session.Client, "PRIA");
        var positionB = await CreatePositionAsync(session.Client, "PRIB");

        await AssignAsync(session.Client, personnelId, positionA, DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(5), isPrimary: true);

        var response = await session.Client.PostAsJsonAsync(
            $"/api/v1/organization/personnel/{personnelId}/positions",
            new { PositionId = positionB, IsPrimary = true, EffectiveFrom = DateTime.UtcNow.AddDays(-1), EffectiveTo = DateTime.UtcNow.AddDays(10) });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("PRIMARY_OVERLAP_CONFLICT");
    }

    [Test]
    public async Task Primary_OtherCompany_CannotAssignPersonnelOfThisCompany()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Create", "Organization.Position.Create", "Organization.PersonnelPosition.Create");
        var personnelId = await CreatePersonnelAsync(session.Client, "1313131313");
        var positionA = await CreatePositionAsync(session.Client, "PRIC");

        var otherCompanyPosition = await WithDbAsync(async db =>
        {
            var otherCompany = new Company("OTHER", "Other Company");
            var position = new Position(otherCompany.Id, "OTHERPRI", "Other Primary");
            db.Companies.Add(otherCompany);
            db.Positions.Add(position);
            await db.SaveChangesAsync();
            return position.Id;
        });

        var otherCompanyId = await WithDbAsync(async db => await db.Positions.Where(p => p.Id == otherCompanyPosition).Select(p => p.CompanyId).SingleAsync());
        var otherSession = await CreateAuthorizedClientAsync("other@test.com", otherCompanyId, "Organization.PersonnelPosition.Create");

        await AssignAsync(session.Client, personnelId, positionA, DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(5), isPrimary: true);
        var response = await otherSession.Client.PostAsJsonAsync(
            $"/api/v1/organization/personnel/{personnelId}/positions",
            new { PositionId = otherCompanyPosition, IsPrimary = true, EffectiveFrom = DateTime.UtcNow.AddDays(-1), EffectiveTo = DateTime.UtcNow.AddDays(10) });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await WithDbAsync(async db =>
            (await db.PersonnelPositions.AsNoTracking().CountAsync(pp => pp.PersonnelId == personnelId && pp.IsPrimary)).ShouldBe(1));
    }

    [Test]
    public async Task Primary_SameCompany_NonOverlap_Allowed()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Create", "Organization.Position.Create", "Organization.PersonnelPosition.Create");
        var personnelId = await CreatePersonnelAsync(session.Client, "1414141414");
        var positionA = await CreatePositionAsync(session.Client, "PRID");
        var positionB = await CreatePositionAsync(session.Client, "PRIE");

        await AssignAsync(session.Client, personnelId, positionA, DateTime.UtcNow.AddDays(-20), DateTime.UtcNow.AddDays(-10), isPrimary: true);
        await AssignAsync(session.Client, personnelId, positionB, DateTime.UtcNow.AddDays(-1), null, isPrimary: true);

        await WithDbAsync(async db =>
            (await db.PersonnelPositions.AsNoTracking().CountAsync(pp => pp.PersonnelId == personnelId && pp.IsPrimary)).ShouldBe(2));
    }
    private async Task<Guid> CreatePersonnelAsync(HttpClient client, string nationalCode)
    {
        var response = await client.PostAsJsonAsync("/api/v1/organization/personnel", new
        {
            NationalCode = nationalCode,
            FirstName = "Test",
            LastName = "Person",
            Gender = 1
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private async Task<Guid> CreatePositionAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/v1/organization/positions", new
        {
            CompanyId = _companyId,
            Code = code,
            Title = code
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private static async Task<Guid> AssignAsync(HttpClient client, Guid personnelId, Guid positionId, DateTime from, DateTime? to, bool isPrimary = false)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/organization/personnel/{personnelId}/positions",
            new { PositionId = positionId, IsPrimary = isPrimary, EffectiveFrom = from, EffectiveTo = to });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private record IdResponse(Guid Id);
    private record CreatePersonnelResponse(Guid Id);
}