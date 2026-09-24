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