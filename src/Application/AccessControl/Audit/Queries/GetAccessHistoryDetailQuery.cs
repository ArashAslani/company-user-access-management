using CompanyAccessManagement.Application.AccessControl.Audit.Queries;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.AccessControl;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CompanyAccessManagement.Application.AccessControl.Audit.Queries;

public record GetAccessHistoryDetailQuery : IRequest<AuditLogDetailDto?>
{
    public Guid OperationId { get; init; }
}

public class GetAccessHistoryDetailQueryHandler : IRequestHandler<GetAccessHistoryDetailQuery, AuditLogDetailDto?>
{
    private readonly IApplicationDbContext _context;

    public GetAccessHistoryDetailQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AuditLogDetailDto?> Handle(GetAccessHistoryDetailQuery request, CancellationToken cancellationToken)
    {
        var logs = await _context.AuditLogs
            .Where(a => a.OperationId == request.OperationId)
            .OrderBy(a => a.Created)
            .ToListAsync(cancellationToken);

        if (!logs.Any())
            return null;

        var first = logs.First();

        var changedModules = new List<ChangedModuleDto>();

        foreach (var log in logs)
        {
            changedModules.Add(new ChangedModuleDto
            {
                ResourceId = log.EntityId,
                ResourceName = log.EntityType,
                SubResourceName = log.EventType,
                Actions = new List<ChangedActionDto>
                {
                    new ChangedActionDto
                    {
                        ActionCode = log.EventType,
                        Before = false,
                        After = true
                    }
                }
            });
        }

        return new AuditLogDetailDto
        {
            ActorUserId = first.ActorUserId,
            ActorFullName = "User " + first.ActorUserId.ToString()[..8],
            ActorRoleTitle = "Role",
            OccurredAt = first.Created,
            ChangeType = first.EventType,
            Source = first.SourceType.ToString(),
            ChangedModules = changedModules
        };
    }
}
