using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Organization.Positions.Queries;
using CleanArchitecture.Application.Organization.Positions.Commands;
using CleanArchitecture.Web.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CleanArchitecture.Web.Endpoints.Organization;

public static class PositionEndpoints
{
    public static void MapPositionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organization/positions")
            .WithTags("Position")
            .RequireAuthorization();

        // 1.1 List positions
        group.MapGet("/", async (
            ISender sender,
            [FromQuery] Guid? companyId = null,
            [FromQuery] string? status = null,
            [FromQuery] string? q = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? sort = null) =>
        {
            var query = new GetPositionsQuery
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
        .RequirePermission("Position.Read")
        .WithName("GetPositions")
        .Produces<PaginatedList<PositionDto>>(StatusCodes.Status200OK);

        // 1.2 Create position
        group.MapPost("/", async (
            CreatePositionCommand command,
            ISender sender) =>
        {
            var id = await sender.Send(command);
            return Results.Created($"/api/v1/organization/positions/{id}", new { id });
        })
        .RequirePermission("Position.Create")
        .WithName("CreatePosition")
        .Produces<Guid>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // 1.3 Update position
        group.MapPut("/{id:guid}", async (
            UpdatePositionCommand command,
            Guid id,
            ISender sender) =>
        {
            await sender.Send(command with { Id = id });
            return Results.Ok();
        })
        .RequirePermission("Position.Edit")
        .WithName("UpdatePosition")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 1.4 Get position details
        group.MapGet("/{id:guid}", async (
            Guid id,
            ISender sender) =>
        {
            var result = await sender.Send(new GetPositionQuery { Id = id });
            return result is not null ? Results.Ok(result) : Results.NotFound();
        })
        .RequirePermission("Position.Read")
        .WithName("GetPosition")
        .Produces<PositionDetailDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 1.5 Delete (soft deactivate) position
        group.MapDelete("/{id:guid}", async (
            Guid id,
            ISender sender) =>
        {
            await sender.Send(new DeletePositionCommand { Id = id });
            return Results.NoContent();
        })
        .RequirePermission("Position.Delete")
        .WithName("DeletePosition")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // 1.6 Tree view
        group.MapGet("/tree", async (
            ISender sender,
            [FromQuery] Guid holdingId) =>
        {
            var result = await sender.Send(new GetPositionTreeQuery { HoldingId = holdingId });
            return Results.Ok(result);
        })
        .RequirePermission("Position.Read")
        .WithName("GetPositionTree")
        .Produces<PositionTreeDto>(StatusCodes.Status200OK);

        // 1.7 Position summary (for tree panel)
        group.MapGet("/{id:guid}/summary", async (
            Guid id,
            ISender sender) =>
        {
            var result = await sender.Send(new GetPositionSummaryQuery { Id = id });
            return result is not null ? Results.Ok(result) : Results.NotFound();
        })
        .RequirePermission("Position.Read")
        .WithName("GetPositionSummary")
        .Produces<PositionSummaryDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}