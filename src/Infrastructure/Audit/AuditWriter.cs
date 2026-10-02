using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.AccessControl;

namespace CompanyAccessManagement.Infrastructure.Audit;

public sealed class AuditOperation : IAuditOperation
{
    public Guid OperationId { get; } = Guid.NewGuid();
}

public sealed class AuditWriter : IAuditWriter
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _user;
    private readonly ICurrentWorkspace _workspace;
    private readonly IAuditOperation _operation;
    private readonly TimeProvider _timeProvider;

    public AuditWriter(IApplicationDbContext context, IUser user, ICurrentWorkspace workspace, IAuditOperation operation, TimeProvider timeProvider)
    {
        _context = context;
        _user = user;
        _workspace = workspace;
        _operation = operation;
        _timeProvider = timeProvider;
    }

    public void Write(AuditWriteRequest request)
    {
        var actorId = _user.Id
            ?? throw new InvalidOperationException("An authenticated user is required to write an audit log.");

        _context.AuditLogs.Add(new AuditLog(
            companyId: request.CompanyId ?? _workspace.CompanyId,
            applicationId: request.ApplicationId,
            actorUserId: actorId,
            operationId: _operation.OperationId,
            entityType: request.EntityType,
            entityId: request.EntityId,
            eventType: request.EventType,
            sourceType: request.SourceType,
            occurredAt: _timeProvider.GetUtcNow().UtcDateTime,
            beforeData: request.BeforeData,
            afterData: request.AfterData,
            metadata: request.Metadata,
            targetPrincipalId: request.TargetPrincipalId,
            permissionId: request.PermissionId));
    }
}
