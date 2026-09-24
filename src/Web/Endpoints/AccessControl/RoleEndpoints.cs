using CompanyAccessManagement.Application.AccessControl.Roles.Commands;
using CompanyAccessManagement.Application.AccessControl.Roles.Commands.BulkAssignRole;
using CompanyAccessManagement.Application.AccessControl.Roles.Queries;
using CompanyAccessManagement.Application.Common.Models;
using CompanyAccessManagement.Web.Authorization;
using CompanyAccessManagement.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CompanyAccessManagement.Web.Endpoints.AccessControl;

public sealed class RoleEndpoints : IEndpointGroup
{
    public static string RoutePrefix => "/api/v1/access-control/roles";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Role")
            .RequireAuthorization();

        // 1.1 List roles
        group.MapGet("/", async (
            ISender sender,
            [FromQuery] Guid? companyId = null,
            [FromQuery] string? status = null,
            [FromQuery] string? q = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? sort = null) =>
        {
            var query = new GetRolesQuery
            {
                CompanyId = companyId,
                Status = status,
                Search = q,
                Page = page,
                PageSize = pageSize,
                Sort = sort
            };
            var result = await sender.Send(query);
            return Results.Ok(result);
        })
        .RequirePermission("AccessManagement.Role.Read")
        .WithName("GetRoles")
        .Produces<PaginatedList<RoleDto>>(StatusCodes.Status200OK);

        // 1.2 Create role
        group.MapPost("/", async (
            CreateRoleCommand command,
            ISender sender) =>
        {
            var id = await sender.Send(command);
            return Results.Created($"/api/v1/access-control/roles/{id}", new { id });
        })
        .RequirePermission("AccessManagement.Role.Create")
        .WithName("CreateRole")
        .Produces<Guid>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // 1.3 Update role
        group.MapPut("/{id:guid}", async (
            UpdateRoleCommand command,
            Guid id,
            ISender sender) =>
        {
            await sender.Send(command with { Id = id });
            return Results.Ok();
        })
        .RequirePermission("AccessManagement.Role.Edit")
        .WithName("UpdateRole")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 1.4 Get role details
        group.MapGet("/{id:guid}", async (
            Guid id,
            ISender sender) =>
        {
            var result = await sender.Send(new GetRoleQuery { Id = id });
            return result is not null ? Results.Ok(result) : Results.NotFound();
        })
        .RequirePermission("AccessManagement.Role.Read")
        .WithName("GetRole")
        .Produces<RoleDetailDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 1.5 Delete role
        group.MapDelete("/{id:guid}", async (
            Guid id,
            ISender sender) =>
        {
            await sender.Send(new DeleteRoleCommand { Id = id });
            return Results.NoContent();
        })
        .RequirePermission("AccessManagement.Role.Delete")
        .WithName("DeleteRole")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // 1.6 Role tree (by holding)
        group.MapGet("/tree", async (
            ISender sender,
            [FromQuery] Guid holdingId) =>
        {
            var result = await sender.Send(new GetRoleTreeQuery { HoldingId = holdingId });
            return Results.Ok(result);
        })
        .RequirePermission("AccessManagement.Role.Read")
        .WithName("GetRoleTree")
        .Produces<RoleTreeDto>(StatusCodes.Status200OK);

        // 1.7 Update role permissions
        group.MapPut("/{id:guid}/permissions", async (
            Guid id,
            UpdateRolePermissionsCommand command,
            ISender sender) =>
        {
            await sender.Send(command with { RoleId = id });
            return Results.Ok();
        })
        .RequirePermission("AccessManagement.Role.Permissions.Manage")
        .WithName("UpdateRolePermissions")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 1.8 Copy role permissions
        group.MapPost("/{id:guid}/permissions/copy", async (
            Guid id,
            CopyRolePermissionsCommand command,
            ISender sender) =>
        {
            await sender.Send(command with { SourceRoleId = id });
            return Results.Ok();
        })
        .RequirePermission("AccessManagement.Role.Permissions.Manage")
        .WithName("CopyRolePermissions")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 1.9 Bulk assign role
        group.MapPost("/bulk-assign", async (
            BulkAssignRoleCommand command,
            ISender sender) =>
        {
            var result = await sender.Send(command);
            return Results.Ok(result);
        })
        .RequirePermission("AccessManagement.Role.BulkAssign")
        .WithName("BulkAssignRole")
        .Produces<BulkAssignRoleResult>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}