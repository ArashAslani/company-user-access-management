using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Models;
using CompanyAccessManagement.Application.Organization.Personnel.Queries;
using CompanyAccessManagement.Application.Organization.Personnel.Commands;
using CompanyAccessManagement.Application.Organization.Personnel.Commands.Signatures;
using CompanyAccessManagement.Web.Authorization;
using CompanyAccessManagement.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PersonnelEntity = CompanyAccessManagement.Domain.Organization.Personnel;

namespace CompanyAccessManagement.Web.Endpoints.Organization;

public sealed class PersonnelEndpoints : IEndpointGroup
{
    public static string RoutePrefix => "/api/v1/organization/personnel";

    // Multipart framing overhead on top of the largest accepted signature.
    private const long SignatureRequestLimitBytes = PersonnelEntity.MaxSignatureBytes + 64 * 1024;

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Personnel")
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
        .RequirePermission("Organization.Personnel.Read")
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
        .RequirePermission("Organization.Personnel.Create")
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
            var assignmentId = await sender.Send(command with { PersonnelId = id });
            return Results.Created($"/api/v1/organization/personnel/{id}/positions/{assignmentId}", new { id = assignmentId });
        })
        .RequirePermission("Organization.PersonnelPosition.Create")
        .WithName("AssignPosition")
        .Produces<Guid>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // 2.4 Update position assignment
        group.MapPut("/{personnelId:guid}/positions/{assignmentId:guid}", async (
            Guid personnelId,
            Guid assignmentId,
            UpdatePositionAssignmentCommand command,
            ISender sender) =>
        {
            await sender.Send(command with { PersonnelId = personnelId, AssignmentId = assignmentId });
            return Results.Ok();
        })
        .RequirePermission("Organization.PersonnelPosition.Edit")
        .WithName("UpdatePositionAssignment")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 2.5 Remove position assignment
        group.MapDelete("/{personnelId:guid}/positions/{assignmentId:guid}", async (
            Guid personnelId,
            Guid assignmentId,
            ISender sender) =>
        {
            await sender.Send(new RemovePositionAssignmentCommand { PersonnelId = personnelId, AssignmentId = assignmentId });
            return Results.NoContent();
        })
        .RequirePermission("Organization.PersonnelPosition.Delete")
        .WithName("RemovePositionAssignment")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // 2.7 Upload/replace signature
        group.MapPost("/{id:guid}/signature", async (
            Guid id,
            IFormFile file,
            ISender sender) =>
        {
            if (file.Length > PersonnelEntity.MaxSignatureBytes)
            {
                return Results.Problem(
                    title: "Signature file exceeds 8MB limit.",
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = "SIGNATURE_TOO_LARGE" });
            }

            using var ms = new MemoryStream((int)file.Length);
            await file.CopyToAsync(ms);
            var content = ms.ToArray();

            var command = new UploadSignatureCommand
            {
                PersonnelId = id,
                FileName = file.FileName,
                ContentType = file.ContentType,
                Content = content
            };
            var signatureId = await sender.Send(command);
            return Results.Created($"/api/v1/organization/personnel/{id}/signature/{signatureId}", new { id = signatureId });
        })
        .RequirePermission("Organization.PersonnelSignature.Create")
        .WithName("UploadSignature")
        .Produces<Guid>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithMetadata(new RequestSizeLimitAttribute(SignatureRequestLimitBytes))
        .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = SignatureRequestLimitBytes })
        .DisableAntiforgery();

        // 2.8 Get personnel details
        group.MapGet("/{id:guid}", async (
            Guid id,
            ISender sender) =>
        {
            var result = await sender.Send(new GetPersonnelDetailQuery { Id = id });
            return result is not null ? Results.Ok(result) : Results.NotFound();
        })
        .RequirePermission("Organization.Personnel.Read")
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
        .RequirePermission("Organization.Personnel.Edit")
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
        .RequirePermission("Organization.Personnel.Delete")
        .WithName("DeletePersonnel")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }
}