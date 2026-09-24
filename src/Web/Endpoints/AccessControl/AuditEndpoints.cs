using CompanyAccessManagement.Application.AccessControl.Audit.Queries;
using CompanyAccessManagement.Application.Common.Models;
using CompanyAccessManagement.Web.Authorization;
using CompanyAccessManagement.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CompanyAccessManagement.Web.Endpoints.AccessControl;

public sealed class AuditEndpoints : IEndpointGroup
{
    public static string RoutePrefix => "/api/v1/audit/access-history";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Audit")
            .RequireAuthorization();

        // 4.1 List/Search access history
        group.MapGet("/", async (
            ISender sender,
            [FromQuery] Guid? actorUserId = null,
            [FromQuery] Guid? companyId = null,
            [FromQuery] DateTimeOffset? dateFrom = null,
            [FromQuery] DateTimeOffset? dateTo = null,
            [FromQuery] string? changeType = null,
            [FromQuery] Guid? resourceId = null,
            [FromQuery] string? source = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var query = new GetAccessHistoryQuery
            {
                ActorUserId = actorUserId,
                CompanyId = companyId,
                DateFrom = dateFrom,
                DateTo = dateTo,
                ChangeType = changeType,
                ResourceId = resourceId,
                Source = source,
                Page = page,
                PageSize = pageSize
            };
            var result = await sender.Send(query);
            return Results.Ok(result);
        })
        .RequirePermission("AuditLog.Read")
        .WithName("GetAccessHistory")
        .Produces<PaginatedList<AuditLogDto>>(StatusCodes.Status200OK);

        // 4.2 Get detail of a specific operation
        group.MapGet("/{operationId:guid}", async (
            Guid operationId,
            ISender sender) =>
        {
            var result = await sender.Send(new GetAccessHistoryDetailQuery { OperationId = operationId });
            return result is not null ? Results.Ok(result) : Results.NotFound();
        })
        .RequirePermission("AuditLog.Read")
        .WithName("GetAccessHistoryDetail")
        .Produces<AuditLogDetailDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 4.3 Export access history
        group.MapGet("/export", async (
            ISender sender,
            [FromQuery] Guid? actorUserId = null,
            [FromQuery] Guid? companyId = null,
            [FromQuery] DateTimeOffset? dateFrom = null,
            [FromQuery] DateTimeOffset? dateTo = null,
            [FromQuery] string? changeType = null,
            [FromQuery] Guid? resourceId = null,
            [FromQuery] string? source = null,
            [FromQuery] string format = "xlsx") =>
        {
            var query = new ExportAccessHistoryQuery
            {
                ActorUserId = actorUserId,
                CompanyId = companyId,
                DateFrom = dateFrom,
                DateTo = dateTo,
                ChangeType = changeType,
                ResourceId = resourceId,
                Source = source,
                Format = format
            };
            var fileBytes = await sender.Send(query);
            var contentType = format == "xlsx" ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "application/pdf";
            var fileName = $"access-history-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{(format == "xlsx" ? "xlsx" : "pdf")}";
            return Results.File(fileBytes, contentType, fileName);
        })
        .RequirePermission("AuditLog.Export")
        .WithName("ExportAccessHistory")
        .Produces<byte[]>(StatusCodes.Status200OK);
    }
}