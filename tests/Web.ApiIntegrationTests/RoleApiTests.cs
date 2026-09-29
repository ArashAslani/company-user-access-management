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
public class RoleApiTests : ApiTestBase
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
    public async Task GetRoles_WithPermission_ReturnsOk()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read");

        var url = $"/api/v1/access-control/roles/?companyId={_companyId}";
        var absoluteUrl = new Uri(session.Client.BaseAddress!, url);
        
        var response = await session.Client.GetAsync(absoluteUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PaginatedList<RoleDto>>();
        result.ShouldNotBeNull();
        result.Items.ShouldNotBeNull();
    }

    [Test]
    public async Task GetRoles_WithoutPermission_ReturnsForbidden()
    {
        var session = await CreateAuthorizedClientAsync(_companyId);

        var response = await session.Client.GetAsync($"/api/v1/access-control/roles/?companyId={_companyId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task CreateRole_WithValidData_ReturnsCreated()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create");

        var command = new
        {
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = "NEWROLE",
            Name = "New Role",
            Kind = RoleKind.Standard,
            ParentRoleId = (Guid?)null
        };

        var response = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles", command);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var result = await response.Content.ReadFromJsonAsync<CreateRoleResponse>();
        result.ShouldNotBeNull();
        result.Id.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task GetRole_ById_ReturnsOk()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create");

        var command = new
        {
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = "TESTROLE",
            Name = "Test Role",
            Kind = RoleKind.Standard,
            ParentRoleId = (Guid?)null
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles", command);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoleResponse>();
        var roleId = createResult!.Id;

        var response = await session.Client.GetAsync($"/api/v1/access-control/roles/{roleId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<RoleDetailDto>();
        result.ShouldNotBeNull();
        result.Code.ShouldBe("TESTROLE");
    }

    [Test]
    public async Task UpdateRole_WithValidData_ReturnsOk()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Edit", "AccessManagement.Role.Create");

        var command = new
        {
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = "TESTROLE",
            Name = "Test Role",
            Kind = RoleKind.Standard,
            ParentRoleId = (Guid?)null
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles", command);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoleResponse>();
        var roleId = createResult!.Id;

        var updateCommand = new
        {
            Id = roleId,
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = "TESTROLE",
            Name = "Updated Role",
            Kind = RoleKind.Standard,
            ParentRoleId = (Guid?)null
        };

        var response = await session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{roleId}", updateCommand);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        await WithDbAsync(async db =>
        {
            var updated = await db.BusinessRoles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == roleId);
            updated!.Name.ShouldBe("Updated Role");
        });
    }

    [Test]
    public async Task DeleteRole_WithNoAssignments_ReturnsNoContent()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Delete", "AccessManagement.Role.Create");

        var command = new
        {
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = "TESTROLE",
            Name = "Test Role",
            Kind = RoleKind.Standard,
            ParentRoleId = (Guid?)null
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles", command);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoleResponse>();
        var roleId = createResult!.Id;

        var response = await session.Client.DeleteAsync($"/api/v1/access-control/roles/{roleId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task GetRoleTree_ReturnsTree()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Create");

        var parentCommand = new
        {
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = "PARENT",
            Name = "Parent Role",
            Kind = RoleKind.Standard,
            ParentRoleId = (Guid?)null
        };

        var parentResponse = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles", parentCommand);
        parentResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var parentResult = await parentResponse.Content.ReadFromJsonAsync<CreateRoleResponse>();
        var parentId = parentResult!.Id;

        var childCommand = new
        {
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = "CHILD",
            Name = "Child Role",
            Kind = RoleKind.Standard,
            ParentRoleId = parentId
        };

        var childResponse = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles", childCommand);
        childResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await session.Client.GetAsync($"/api/v1/access-control/roles/tree?holdingId={_companyId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<RoleTreeDto>();
        result.ShouldNotBeNull();
    }

    [Test]
    public async Task UpdateRolePermissions_WithValidData_ReturnsOk()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "AccessManagement.Role.Read", "AccessManagement.Role.Permissions.Manage", "AccessManagement.Role.Create");

        var roleCommand = new
        {
            CompanyId = _companyId,
            ApplicationId = _appId,
            Code = "TESTROLE",
            Name = "Test Role",
            Kind = RoleKind.Standard,
            ParentRoleId = (Guid?)null
        };

        var createResponse = await session.Client.PostAsJsonAsync("/api/v1/access-control/roles", roleCommand);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoleResponse>();
        var roleId = createResult!.Id;

        var permCommand = new
        {
            RoleId = roleId,
            PermissionIds = new Guid[0]
        };

        var response = await session.Client.PutAsJsonAsync($"/api/v1/access-control/roles/{roleId}/permissions", permCommand);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private record CreateRoleResponse(Guid Id);
}