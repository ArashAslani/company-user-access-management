using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Models;
using CompanyAccessManagement.Application.Organization.Personnel.Queries;
using CompanyAccessManagement.Application.Organization.Personnel.Commands;
using CompanyAccessManagement.Web.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyAccessManagement.Web.Endpoints.Organization;

public static class PersonnelEndpoints
{
    public static void MapPersonnelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organization/personnel")
            .WithTags("Personnel")
            .RequireAuthorization();

        // 2.1 List personnel
        group.MapGet("/", async (
            ISender sender,
            [FromQuery] Guid? companyId = null,
            [FromQuery] string? status = null,
            [FromQuery] string? q = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? sort = null) =>
        {
            var query = new GetPersonnelQuery
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
        .RequirePermission("Personnel.Read")
        .WithName("GetPersonnel")
        .Produces<PaginatedList<PersonnelDto>>(StatusCodes.Status200OK);

        // 2.2 Create personnel (step 1: base info)
        group.MapPost("/", async (
            CreatePersonnelCommand command,
            ISender sender) =>
        {
            var id = await sender.Send(command);
            return Results.Created($"/api/v1/organization/personnel/{id}", new { id });
        })
        .RequirePermission("Personnel.Create")
        .WithName("CreatePersonnel")
        .Produces<Guid>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // 2.3 Assign position (step 2)
        group.MapPost("/{id:guid}/positions", async (
            Guid id,
            AssignPositionCommand command,
            ISender sender) =>
        {
            await sender.Send(command with { PersonnelId = id });
            return Results.Ok();
        })
        .RequirePermission("PersonnelPosition.Create")
        .WithName("AssignPosition")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // 2.4 Update position assignment
        group.MapPut("/{personnelId:guid}/positions/{personnelPositionId:guid}", async (
            Guid personnelId,
            Guid personnelPositionId,
            UpdatePositionAssignmentCommand command,
            ISender sender) =>
        {
            await sender.Send(command with { PersonnelId = personnelId, PersonnelPositionId = personnelPositionId });
            return Results.Ok();
        })
        .RequirePermission("PersonnelPosition.Edit")
        .WithName("UpdatePositionAssignment")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 2.5 Remove position assignment
        group.MapDelete("/{personnelId:guid}/positions/{personnelPositionId:guid}", async (
            Guid personnelId,
            Guid personnelPositionId,
            ISender sender) =>
        {
            await sender.Send(new RemovePositionAssignmentCommand { PersonnelId = personnelId, PersonnelPositionId = personnelPositionId });
            return Results.NoContent();
        })
        .RequirePermission("PersonnelPosition.Delete")
        .WithName("RemovePositionAssignment")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 2.7 Upload/replace signature
        group.MapPost("/{id:guid}/signature", async (
            Guid id,
            IFormFile file,
            ISender sender) =>
        {
            // In a real implementation, we'd handle multipart form data
            // For now, return not implemented
            return Results.Problem("Not implemented", statusCode: StatusCodes.Status501NotImplemented);
        })
        .RequirePermission("PersonnelSignature.Create")
        .WithName("UploadSignature")
        .Produces<Guid>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // 2.8 Get personnel details
        group.MapGet("/{id:guid}", async (
            Guid id,
            ISender sender) =>
        {
            var result = await sender.Send(new GetPersonnelDetailQuery { Id = id });
            return result is not null ? Results.Ok(result) : Results.NotFound();
        })
        .RequirePermission("Personnel.Read")
        .WithName("GetPersonnelDetail")
        .Produces<PersonnelDetailDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 2.9 Update personnel base info
        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdatePersonnelCommand command,
            ISender sender) =>
        {
            await sender.Send(command with { Id = id });
            return Results.Ok();
        })
        .RequirePermission("Personnel.Edit")
        .WithName("UpdatePersonnel")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 2.10 Delete personnel
        group.MapDelete("/{id:guid}", async (
            Guid id,
            ISender sender) =>
        {
            await sender.Send(new DeletePersonnelCommand { Id = id });
            return Results.NoContent();
        })
        .RequirePermission("Personnel.Delete")
        .WithName("DeletePersonnel")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
