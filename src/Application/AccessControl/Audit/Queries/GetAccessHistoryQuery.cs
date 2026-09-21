using CleanArchitecture.Application.AccessControl.Audit.Queries;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Domain.AccessControl;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.AccessControl.Audit.Queries;

public record GetAccessHistoryQuery : IRequest<PaginatedList<AuditLogDto>>
{
    public Guid? ActorUserId { get; init; }
    public Guid? CompanyId { get; init; }
    public DateTimeOffset? DateFrom { get; init; }
    public DateTimeOffset? DateTo { get; init; }
    public string? ChangeType { get; init; }
    public Guid? ResourceId { get; init; }
    public string? Source { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetAccessHistoryQueryHandler : IRequestHandler<GetAccessHistoryQuery, PaginatedList<AuditLogDto>>
{
    private readonly IApplicationDbContext _context;

    public GetAccessHistoryQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<AuditLogDto>> Handle(GetAccessHistoryQuery request, CancellationToken cancellationToken)
    {
        var query = _context.AuditLogs.AsQueryable();

        if (request.ActorUserId.HasValue)
            query = query.Where(a => a.ActorUserId == request.ActorUserId.Value);

        if (request.CompanyId.HasValue)
            query = query.Where(a => a.CompanyId == request.CompanyId.Value);

        if (request.DateFrom.HasValue)
            query = query.Where(a => a.Created >= request.DateFrom.Value);

        if (request.DateTo.HasValue)
            query = query.Where(a => a.Created <= request.DateTo.Value);

        if (!string.IsNullOrWhiteSpace(request.ChangeType))
            query = query.Where(a => a.EventType == request.ChangeType);

        if (request.ResourceId.HasValue)
            query = query.Where(a => a.EntityId == request.ResourceId.Value);

        if (!string.IsNullOrWhiteSpace(request.Source))
            query = query.Where(a => a.SourceType.ToString() == request.Source);

        query = query.OrderByDescending(a => a.Created);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(a => new AuditLogDto
            {
                OperationId = a.OperationId,
                ActorUserId = a.ActorUserId,
                ActorFullName = "User " + a.ActorUserId.ToString().Substring(0, 8),
                ActorRoleTitle = "Role",
                OccurredAt = a.Created,
                ChangeType = a.EventType,
                Source = a.SourceType.ToString(),
                ResourceSummary = $"{a.EntityType}: {a.EntityId}"
            })
            .ToListAsync(cancellationToken);

        return new PaginatedList<AuditLogDto>(items, totalCount, request.Page, request.PageSize);
    }
}