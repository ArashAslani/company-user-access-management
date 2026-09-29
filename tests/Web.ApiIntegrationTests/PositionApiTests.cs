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
public class PositionApiTests : ApiTestBase
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
    public async Task GetPositions_WithPermission_ReturnsOk()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Read", "Organization.Position.Create");

        // Create a position via API
        var createCommand = new
        {
            CompanyId = _companyId,
            Code = "TESTPOS",
            Title = "Test Position",
            Description = "Test Description",
            Kind = "Organizational",
            HoldingId = _companyId
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/positions", createCommand);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreatePositionResponse>();
        var positionId = createResult!.Id;

        var url = $"/api/v1/organization/positions/?companyId={_companyId}";
        var absoluteUrl = new Uri(session.Client.BaseAddress!, url);
        
        var response = await session.Client.GetAsync(absoluteUrl);
        
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PaginatedList<PositionDto>>();
        result.ShouldNotBeNull();
        result.Items.ShouldNotBeNull();
        result.Items.Count.ShouldBe(1);
    }

    [Test]
    public async Task GetPositions_WithoutPermission_ReturnsForbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId);

        var response = await session.Client.GetAsync($"/api/v1/organization/positions/?companyId={_companyId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task CreatePosition_WithValidData_ReturnsCreated()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Create");

        var command = new
        {
            CompanyId = _companyId,
            Code = "NEWPOS",
            Title = "New Position",
            Description = "New Position Description",
            Kind = "Organizational",
            HoldingId = _companyId
        };

        var response = await session.Client.PostAsJsonAsync("/api/v1/organization/positions", command);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var result = await response.Content.ReadFromJsonAsync<CreatePositionResponse>();
        result.ShouldNotBeNull();
        result.Id.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task Debug_AccessEvaluator_Works()
    {
        await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Create");

        // Debug step by step what the AccessEvaluator does
        var evaluatorScope = Factory.Services.CreateScope();
        var scope = evaluatorScope.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        
        // 1. Get user
        var user = await WithDbAsync(async db => 
            await db.Users.FirstOrDefaultAsync(u => u.UserName == "test@test.com"));
        
        Console.WriteLine($"User ID: {user?.Id}");

        // 1. Check UserCompany membership
        var userCompany = await db.UserCompanies
            .Include(uc => uc.Roles)
            .FirstOrDefaultAsync(uc => uc.UserId == user!.Id && uc.CompanyId == _companyId && uc.Status == UserCompanyStatus.Active);
        
        Console.WriteLine($"UserCompany found: {userCompany != null}");
        if (userCompany != null)
        {
            Console.WriteLine($"  UserCompany.PrincipalId: {userCompany.PrincipalId}");
            Console.WriteLine($"  UserCompany.Roles count: {userCompany.Roles.Count}");
        }

        // 2. Check Permission
        var application = await db.Applications.FirstOrDefaultAsync(a => a.Code == "QC" && a.IsActive);
        Console.WriteLine($"Application found: {application != null}");
        
        var permission = await (from p in db.Permissions
            join r in db.Resources on p.ResourceId equals r.Id
            where r.ApplicationId == application!.Id && r.Code + "." + p.ActionCode == "Organization.Position.Create"
            select p).FirstOrDefaultAsync();
        
        Console.WriteLine($"Permission found: {permission != null}");
        if (permission != null)
        {
            Console.WriteLine($"  Permission.Id: {permission.Id}");
            Console.WriteLine($"  Permission.ActionCode: {permission.ActionCode}");
        }

        // 3. Check AccessRules for this principal/permission
        var principalId = userCompany?.PrincipalId ?? Guid.Empty;
        Console.WriteLine($"PrincipalId for AccessRule query: {principalId}");
        
        var directRules = await db.AccessRules
            .Where(ar => ar.PrincipalId == principalId 
                && ar.PermissionId == permission!.Id 
                && ar.Effect == AccessEffect.Allow
                && ar.Origin != AccessRuleOrigin.Delegated
                && ar.Status == AccessRuleStatus.Active
                && (ar.ValidFrom == null || ar.ValidFrom <= DateTime.UtcNow)
                && (ar.ValidUntil == null || ar.ValidUntil > DateTime.UtcNow))
            .Include(ar => ar.Scopes)
            .ToListAsync();
        
        Console.WriteLine($"Direct rules found: {directRules.Count}");
        foreach (var r in directRules)
        {
            Console.WriteLine($"  Rule: Id={r.Id}, Effect={r.Effect}, Origin={r.Origin}, PrincipalId={r.PrincipalId}, PermissionId={r.PermissionId}, ValidFrom={r.ValidFrom}, ValidUntil={r.ValidUntil}");
        }

        // Now try the actual evaluator
        var request = new AccessRequest(
            user!.Id,
            _companyId,
            "QC",
            "Organization.Position.Create"
        );
        
        var decision = await evaluatorScope.ServiceProvider.GetRequiredService<IAccessEvaluator>().EvaluateAsync(request);
        Console.WriteLine($"Access Decision: Allowed={decision.Allowed}, Reason={decision.ReasonCode}");
        foreach (var source in decision.Sources)
        {
            Console.WriteLine($"  Source: SourceType={source.SourceType}, SourceId={source.SourceId}, ScopeMode={source.ScopeMode}");
        }

        decision.Allowed.ShouldBeTrue();
    }

    [Test]
    public async Task GetPosition_ById_ReturnsOk()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Read", "Organization.Position.Create");

        var createCommand = new
        {
            CompanyId = _companyId,
            Code = "GETPOS",
            Title = "Get Position",
            Description = "Get Desc",
            Kind = "Organizational",
            HoldingId = _companyId
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/positions", createCommand);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreatePositionResponse>();
        var positionId = createResult!.Id;

        var response = await session.Client.GetAsync($"/api/v1/organization/positions/{positionId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PositionDetailDto>();
        result.ShouldNotBeNull();
        result.Code.ShouldBe("GETPOS");
    }

    [Test]
    public async Task GetPosition_NotFound_ReturnsNotFound()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Read");

        var response = await session.Client.GetAsync($"/api/v1/organization/positions/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UpdatePosition_WithValidData_ReturnsOk()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Read", "Organization.Position.Edit", "Organization.Position.Create");

        var createCommand = new
        {
            CompanyId = _companyId,
            Code = "UPDPOS",
            Title = "Update Position",
            Description = "Update Desc",
            Kind = "Organizational",
            HoldingId = _companyId
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/positions", createCommand);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreatePositionResponse>();
        var positionId = createResult!.Id;

        var command = new
        {
            Id = positionId,
            CompanyId = _companyId,
            Code = "UPDPOS",
            Title = "Updated Position",
            Description = "Updated Desc",
            Kind = "Organizational",
            HoldingId = _companyId
        };

        var response = await session.Client.PutAsJsonAsync($"/api/v1/organization/positions/{positionId}", command);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        await WithDbAsync(async db =>
        {
            var updated = await db.Positions
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == positionId);
            updated!.Title.ShouldBe("Updated Position");
        });
    }

    [Test]
    public async Task DeletePosition_WithNoAssignments_ReturnsNoContent()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Read", "Organization.Position.Delete", "Organization.Position.Create");

        var createCommand = new
        {
            CompanyId = _companyId,
            Code = "DELPOS",
            Title = "Delete Position",
            Description = "Delete Desc",
            Kind = "Organizational",
            HoldingId = _companyId
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/positions", createCommand);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreatePositionResponse>();
        var positionId = createResult!.Id;

        var response = await session.Client.DeleteAsync($"/api/v1/organization/positions/{positionId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task DeletePosition_WithActiveAssignments_ReturnsConflict()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Read", "Organization.Position.Delete", "Organization.Position.Create", "Organization.Personnel.Create", "Organization.PersonnelPosition.Create");

        var createCommand = new
        {
            CompanyId = _companyId,
            Code = "DELPOS2",
            Title = "Delete Position 2",
            Description = "Delete Desc",
            Kind = "Organizational",
            HoldingId = _companyId
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/positions", createCommand);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreatePositionResponse>();
        var positionId = createResult!.Id;

        // Create personnel and assign position
        var personnelCreate = new
        {
            CompanyId = _companyId,
            NationalCode = "1234567890",
            FirstName = "John",
            LastName = "Doe",
            Gender = 1,
            PersonnelCode = "P001",
            PhoneNumber = "555-1234"
        };

        var personnelResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/personnel", personnelCreate);
        personnelResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var personnelResult = await personnelResponse.Content.ReadFromJsonAsync<CreatePersonnelResponse>();
        var personnelId = personnelResult!.Id;

        var assignCommand = new
        {
            PersonnelId = personnelId,
            PositionId = positionId,
            IsPrimary = true,
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            EffectiveTo = (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(10)
        };

        var assignResponse = await session.Client.PostAsJsonAsync($"/api/v1/organization/personnel/{personnelId}/positions", assignCommand);
        assignResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await session.Client.DeleteAsync($"/api/v1/organization/positions/{positionId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task GetPositionTree_ReturnsTree()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Read", "Organization.Position.Create");

        var parentCommand = new
        {
            CompanyId = _companyId,
            Code = "PARENT",
            Title = "Parent Position",
            Description = "Parent",
            Kind = "Organizational",
            HoldingId = _companyId
        };

        var parentResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/positions", parentCommand);
        parentResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var parentResult = await parentResponse.Content.ReadFromJsonAsync<CreatePositionResponse>();
        var parentId = parentResult!.Id;

        var childCommand = new
        {
            CompanyId = _companyId,
            Code = "CHILD",
            Title = "Child Position",
            Description = "Child",
            Kind = "Organizational",
            HoldingId = _companyId,
            ParentPositionId = parentId
        };

        var childResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/positions", childCommand);
        childResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await session.Client.GetAsync($"/api/v1/organization/positions/tree?holdingId={_companyId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PositionTreeDto>();
        result.ShouldNotBeNull();
    }

    [Test]
    public async Task GetPositionSummary_ReturnsSummary()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Read", "Organization.Position.Create");

        var createCommand = new
        {
            CompanyId = _companyId,
            Code = "SUMMPOS",
            Title = "Summary Position",
            Description = "Summary Desc",
            Kind = "Organizational",
            HoldingId = _companyId
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/organization/positions", createCommand);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreatePositionResponse>();
        var positionId = createResult!.Id;

        var response = await session.Client.GetAsync($"/api/v1/organization/positions/{positionId}/summary");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PositionSummaryDto>();
        result.ShouldNotBeNull();
        result.Id.ShouldBe(positionId);
    }

    private record CreatePositionResponse(Guid Id);
}