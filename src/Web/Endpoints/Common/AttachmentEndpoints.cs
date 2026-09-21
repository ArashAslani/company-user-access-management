using CleanArchitecture.Application.Common.Attachments;
using CleanArchitecture.Application.Common.Attachments.Commands;
using CleanArchitecture.Web.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitecture.Web.Endpoints.Common;

public static class AttachmentEndpoints
{
    public static void MapAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/attachments")
            .WithTags("Attachments")
            .RequireAuthorization();

        // Upload attachment
        group.MapPost("/", async (
            IFormFile file,
            [FromForm] Guid entityType,
            [FromForm] Guid entityId,
            [FromForm] string? description,
            ISender sender) =>
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            
            var command = new UploadAttachmentCommand
            {
                FileName = file.FileName,
                ContentType = file.ContentType,
                Content = ms.ToArray(),
                EntityType = entityType,
                EntityId = entityId,
                Description = description ?? string.Empty
            };

            var result = await sender.Send(command);
            return Results.Created($"/api/v1/attachments/{result.Id}", result);
        })
        .RequirePermission("Attachment.Create")
        .WithName("UploadAttachment")
        .Produces<AttachmentDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .DisableAntiforgery();

        // Download attachment
        group.MapGet("/{id:guid}", async (
            Guid id,
            ISender sender) =>
        {
            // In real implementation, fetch from storage
            return Results.NotFound();
        })
        .RequirePermission("Attachment.Read")
        .WithName("GetAttachment")
        .Produces<AttachmentDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // Delete attachment
        group.MapDelete("/{id:guid}", async (
            Guid id,
            ISender sender) =>
        {
            // In real implementation, delete from storage
            return Results.NoContent();
        })
        .RequirePermission("Attachment.Delete")
        .WithName("DeleteAttachment")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}