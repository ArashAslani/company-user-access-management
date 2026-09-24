using CompanyAccessManagement.Domain.Common;

namespace CompanyAccessManagement.Domain.AccessControl;

public sealed class AuditLog : BaseAuditableEntity<Guid>
{
    public Guid? CompanyId { get; private set; }
    public Guid? ApplicationId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public Guid OperationId { get; private set; }
    public string EntityType { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    public Guid? TargetPrincipalId { get; private set; }
    public Guid? PermissionId { get; private set; }
    public AccessRuleSourceType SourceType { get; private set; }
    public string EventType { get; private set; } = null!;
    public string? BeforeData { get; private set; }
    public string? AfterData { get; private set; }
    public string? Metadata { get; private set; }

    private AuditLog() { }

    public AuditLog(Guid? companyId, Guid? applicationId, Guid actorUserId, Guid operationId, string entityType, Guid entityId, string eventType,
        AccessRuleSourceType sourceType, string? beforeData = null, string? afterData = null, string? metadata = null,
        Guid? targetPrincipalId = null, Guid? permissionId = null)
    {
        CompanyId = companyId;
        ApplicationId = applicationId;
        ActorUserId = actorUserId;
        OperationId = operationId;
        EntityType = entityType;
        EntityId = entityId;
        EventType = eventType;
        SourceType = sourceType;
        BeforeData = beforeData;
        AfterData = afterData;
        Metadata = metadata;
        TargetPrincipalId = targetPrincipalId;
        PermissionId = permissionId;
        Created = DateTimeOffset.UtcNow;
    }
}

public enum AccessRuleSourceType { DirectUser, Role, RoleGroup, Delegated, System, Copy }
