using CompanyAccessManagement.Application.Common.Attachments;
using CompanyAccessManagement.Application.Common.Attachments.Commands;
using CompanyAccessManagement.Web.Authorization;
using CompanyAccessManagement.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CompanyAccessManagement.Web.Endpoints.Common;

public sealed class AttachmentEndpoints : IEndpointGroup
{
    public static string RoutePrefix => "/api/v1/attachments";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Attachments")
            .RequireAuthorization();

        // Upload attachment - NOT IMPLEMENTED
        // Attachment entity exists in domain but is not fully integrated with persistence layer
        group.MapPost("/", async (
            IFormFile file,
            [FromForm] Guid entityType,
            [FromForm] Guid entityId,
            [FromForm] string? description) =>
        {
            return Results.Problem(
                "Attachment upload is not yet implemented. The Attachment domain entity exists but persistence integration is pending.",
                statusCode: StatusCodes.Status501NotImplemented);
        })
        .RequirePermission("Attachment.Create")
        .WithName("UploadAttachment")
        .ProducesProblem(StatusCodes.Status501NotImplemented)
        .DisableAntiforgery();

        // Download attachment - NOT IMPLEMENTED
        group.MapGet("/{id:guid}", async (Guid id) =>
        {
            return Results.Problem(
                "Attachment download is not yet implemented.",
                statusCode: StatusCodes.Status501NotImplemented);
        })
        .RequirePermission("Attachment.Read")
        .WithName("GetAttachment")
        .ProducesProblem(StatusCodes.Status501NotImplemented);

        // Delete attachment - NOT IMPLEMENTED
        group.MapDelete("/{id:guid}", async (Guid id) =>
        {
            return Results.Problem(
                "Attachment deletion is not yet implemented.",
                statusCode: StatusCodes.Status501NotImplemented);
        })
        .RequirePermission("Attachment.Delete")
        .WithName("DeleteAttachment")
        .ProducesProblem(StatusCodes.Status501NotImplemented);
    }
}