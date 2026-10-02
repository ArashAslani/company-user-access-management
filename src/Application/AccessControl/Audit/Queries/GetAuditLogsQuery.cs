using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Models;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;

namespace CompanyAccessManagement.Application.AccessControl.Audit.Queries;

public sealed record GetAuditLogsQuery : IRequest<PaginatedList<AuditLogDto>>
{
    public Guid? ActorUserId { get; init; }
    public Guid? CompanyId { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public string? EntityType { get; init; }
    public string? EventType { get; init; }
    public Guid? OperationId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record AuditLogDto(
    Guid Id,
    Guid? CompanyId,
    Guid? ApplicationId,
    Guid ActorUserId,
    Guid OperationId,
    string EntityType,
    Guid EntityId,
    Guid? TargetPrincipalId,
    Guid? PermissionId,
    AccessRuleSourceType SourceType,
    string EventType,
    string? BeforeData,
    string? AfterData,
    string? Metadata,
    DateTime OccurredAt);

public sealed class GetAuditLogsQueryValidator : AbstractValidator<GetAuditLogsQuery>
{
    public GetAuditLogsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From.HasValue && x.To.HasValue);
    }
}

public sealed class GetAuditLogsQueryHandler : IRequestHandler<GetAuditLogsQuery, PaginatedList<AuditLogDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;

    public GetAuditLogsQueryHandler(IApplicationDbContext context, ICurrentWorkspace workspace)
    {
        _context = context;
        _workspace = workspace;
    }

    public async Task<PaginatedList<AuditLogDto>> Handle(GetAuditLogsQuery request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.EnsureCompany(request.CompanyId);
        var query = _context.AuditLogs.AsNoTracking().Where(x => x.CompanyId == companyId);
        if (request.ActorUserId.HasValue)
            query = query.Where(x => x.ActorUserId == request.ActorUserId);
        if (request.From is DateTime from)
        {
            var fromUtc = from.ToUniversalTime();
            query = query.Where(x => x.OccurredAt >= fromUtc);
        }
        if (request.To is DateTime to)
        {
            var toUtc = to.ToUniversalTime();
            query = query.Where(x => x.OccurredAt <= toUtc);
        }
        if (!string.IsNullOrWhiteSpace(request.EntityType))
            query = query.Where(x => x.EntityType == request.EntityType);
        if (!string.IsNullOrWhiteSpace(request.EventType))
            query = query.Where(x => x.EventType == request.EventType);
        if (request.OperationId.HasValue)
            query = query.Where(x => x.OperationId == request.OperationId);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new AuditLogDto(
                x.Id, x.CompanyId, x.ApplicationId, x.ActorUserId, x.OperationId,
                x.EntityType, x.EntityId, x.TargetPrincipalId, x.PermissionId,
                x.SourceType, x.EventType, x.BeforeData, x.AfterData, x.Metadata, x.OccurredAt))
            .ToListAsync(cancellationToken);
        return new PaginatedList<AuditLogDto>(items, total, request.Page, request.PageSize);
    }
}
