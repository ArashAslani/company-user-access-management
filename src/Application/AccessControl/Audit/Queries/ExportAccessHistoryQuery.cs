using CleanArchitecture.Application.AccessControl.Audit.Queries;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.AccessControl;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace CleanArchitecture.Application.AccessControl.Audit.Queries;

public record ExportAccessHistoryQuery : IRequest<byte[]>
{
    public Guid? ActorUserId { get; init; }
    public Guid? CompanyId { get; init; }
    public DateTimeOffset? DateFrom { get; init; }
    public DateTimeOffset? DateTo { get; init; }
    public string? ChangeType { get; init; }
    public Guid? ResourceId { get; init; }
    public string? Source { get; init; }
    public string Format { get; init; } = "xlsx";
}

public class ExportAccessHistoryQueryHandler : IRequestHandler<ExportAccessHistoryQuery, byte[]>
{
    private readonly IApplicationDbContext _context;

    public ExportAccessHistoryQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<byte[]> Handle(ExportAccessHistoryQuery request, CancellationToken cancellationToken)
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

        var logs = await query.ToListAsync(cancellationToken);

        if (request.Format == "xlsx")
        {
            var csv = new StringBuilder();
            csv.AppendLine("OperationId,ActorUserId,OccurredAt,ChangeType,Source,EntityType,EntityId,EventType");
            foreach (var log in logs)
            {
                csv.AppendLine($"\"{log.OperationId}\",\"{log.ActorUserId}\",\"{log.Created:O}\",\"{log.EventType}\",\"{log.SourceType}\",\"{log.EntityType}\",\"{log.EntityId}\",\"{log.EventType}\"");
            }
            return Encoding.UTF8.GetBytes(csv.ToString());
        }

        var text = new StringBuilder();
        text.AppendLine("Access History Report");
        text.AppendLine($"Generated: {DateTimeOffset.UtcNow:O}");
        text.AppendLine();
        foreach (var log in logs)
        {
            text.AppendLine($"{log.Created:O} | {log.ActorUserId} | {log.EventType} | {log.SourceType} | {log.EntityType}:{log.EntityId}");
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }
}