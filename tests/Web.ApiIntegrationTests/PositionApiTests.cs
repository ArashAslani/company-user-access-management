using CompanyAccessManagement.Application.Common.Models;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Organization;
using CompanyAccessManagement.Infrastructure.Data;
using CompanyAccessManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private Guid _testUserId;
    private Guid _testPrincipalId;
    private string _authToken = null!;

    [SetUp]
    public override async Task SetUp()
    {
        // Don't call base.SetUp() which cleans the database and deletes seeder's data
        Factory = new ApiTestWebApplicationFactory();
        Client = Factory.CreateClient();  // Create client first to trigger EnsureCreated
        
        // Ensure database schema is created
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
        }
        
        Scope = Factory.Services.CreateScope();
        Context = Scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        UserManager = Scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        RoleManager = Scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        // Manually seed test data (seeder doesn't run reliably in test environment)
        (_companyId, _appId, _productsReadPermId, _productsEditPermId, _productsDeletePermId) = await SeedTestDataAsync();

        // Create test user
        var user = await CreateUserAsync("test@test.com", "Test123!");
        _testUserId = user.Id;

        // Create user company with principal
        var principalId = Guid.NewGuid();
        var userCompany = await CreateUserCompanyAsync(_testUserId, _companyId, principalId);

        // Create AuthPrincipal for the UserCompany
        var authPrincipal = await CreateAuthPrincipalForUserCompanyAsync(userCompany.Id, _companyId, _appId);
        _testPrincipalId = authPrincipal.Id;

        // Sync UserCompany PrincipalId
        if (Context is ApplicationDbContext dbContext)
        {
            dbContext.Entry(userCompany).Property("PrincipalId").CurrentValue = _testPrincipalId;
            await dbContext.SaveChangesAsync(default);
        }

        // Grant permissions
        await CreateAccessRuleAsync(_testPrincipalId, _productsReadPermId, AccessEffect.Allow);
        await CreateAccessRuleAsync(_testPrincipalId, _productsEditPermId, AccessEffect.Allow);
        await CreateAccessRuleAsync(_testPrincipalId, _productsDeletePermId, AccessEffect.Allow);

        // Login
        _authToken = await LoginAsync("test@test.com", "Test123!");
        SetAuthHeader(_authToken);
    }

    [Test]
    public async Task GetPositions_WithPermission_ReturnsOk()
    {
        var response = await Client.GetAsync("/api/v1/organization/positions");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PaginatedList<PositionDto>>();
        result.ShouldNotBeNull();
        result.Items.ShouldNotBeNull();
    }

    [Test]
    public async Task GetPositions_WithoutPermission_ReturnsForbidden()
    {
        // Remove permissions
        var rules = await Context.AccessRules.Where(r => r.PrincipalId == _testPrincipalId).ToListAsync();
        Context.AccessRules.RemoveRange(rules);
        await Context.SaveChangesAsync(default);

        var response = await Client.GetAsync("/api/v1/organization/positions");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task CreatePosition_WithValidData_ReturnsCreated()
    {
        var command = new
        {
            CompanyId = _companyId,
            Code = "NEWPOS",
            Title = "New Position",
            Description = "New Position Description"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/organization/positions", command);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var result = await response.Content.ReadFromJsonAsync<CreatePositionResponse>();
        result.ShouldNotBeNull();
        result.Id.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task GetPosition_ById_ReturnsOk()
    {
        var pos = new Position(_companyId, "GETPOS", "Get Position", "Get Desc");
        Context.Positions.Add(pos);
        await Context.SaveChangesAsync(default);

        var response = await Client.GetAsync($"/api/v1/organization/positions/{pos.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PositionDetailDto>();
        result.ShouldNotBeNull();
        result.Code.ShouldBe("GETPOS");
    }

    [Test]
    public async Task GetPosition_NotFound_ReturnsNotFound()
    {
        var response = await Client.GetAsync($"/api/v1/organization/positions/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UpdatePosition_WithValidData_ReturnsOk()
    {
        var pos = new Position(_companyId, "UPDPOS", "Update Position", "Update Desc");
        Context.Positions.Add(pos);
        await Context.SaveChangesAsync(default);

        var command = new
        {
            Id = pos.Id,
            CompanyId = _companyId,
            Code = "UPDPOS",
            Title = "Updated Position",
            Description = "Updated Desc"
        };

        var response = await Client.PutAsJsonAsync($"/api/v1/organization/positions/{pos.Id}", command);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        var updated = await Context.Positions.FindAsync(pos.Id);
        updated!.Title.ShouldBe("Updated Position");
    }

    [Test]
    public async Task DeletePosition_WithNoAssignments_ReturnsNoContent()
    {
        var pos = new Position(_companyId, "DELPOS", "Delete Position", "Delete Desc");
        Context.Positions.Add(pos);
        await Context.SaveChangesAsync(default);

        var response = await Client.DeleteAsync($"/api/v1/organization/positions/{pos.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        
        var deleted = await Context.Positions.FindAsync(pos.Id);
        deleted!.Status.ShouldBe(PositionStatus.Inactive);
    }

    [Test]
    public async Task DeletePosition_WithActiveAssignments_ReturnsConflict()
    {
        var pos = new Position(_companyId, "DELPOS2", "Delete Position 2", "Delete Desc");
        Context.Positions.Add(pos);
        await Context.SaveChangesAsync(default);

        // Create personnel and assign position
        var personnel = new Personnel("1234567890", "John", "Doe", Gender.Male, "P001", "555-1234");
        Context.Personnel.Add(personnel);
        await Context.SaveChangesAsync(default);

        personnel.AssignPosition(pos.Id, true, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(10));
        await Context.SaveChangesAsync(default);

        var response = await Client.DeleteAsync($"/api/v1/organization/positions/{pos.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private record CreatePositionResponse(Guid Id);
}

// DTOs matching the API responses
public record PaginatedList<T>(List<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public record PositionDto(Guid Id, string Code, string Title, string? Description, Guid? ParentPositionId, PositionStatus Status);

public record PositionDetailDto(
    Guid Id,
    string Code,
    string Title,
    string? Description,
    Guid CompanyId,
    Guid? ParentPositionId,
    PositionStatus Status,
    DateTimeOffset Created,
    DateTimeOffset? LastModified);