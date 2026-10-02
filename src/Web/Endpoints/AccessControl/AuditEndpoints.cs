using CompanyAccessManagement.Application.AccessControl.Audit.Queries;
using CompanyAccessManagement.Application.Common.Models;
using CompanyAccessManagement.Web.Authorization;
using CompanyAccessManagement.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace CompanyAccessManagement.Web.Endpoints.AccessControl;

public sealed class AuditEndpoints : IEndpointGroup
{
    public static string RoutePrefix => "/api/v1/audit/access-history";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Audit").RequireAuthorization();
        group.MapGet("/", async (
            ISender sender,
            [FromQuery] Guid? actorUserId = null,
            [FromQuery] Guid? companyId = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] string? entityType = null,
            [FromQuery] string? eventType = null,
            [FromQuery] Guid? operationId = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var result = await sender.Send(new GetAuditLogsQuery
            {
                ActorUserId = actorUserId,
                CompanyId = companyId,
                From = from,
                To = to,
                EntityType = entityType,
                EventType = eventType,
                OperationId = operationId,
                Page = page,
                PageSize = pageSize
            });
            return Results.Ok(result);
        })
        .RequirePermission("AccessManagement.AuditLog.Read")
        .WithName("GetAuditLogs")
        .Produces<PaginatedList<AuditLogDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
