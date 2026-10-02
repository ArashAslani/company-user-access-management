using CompanyAccessManagement.Domain.AccessControl;

namespace CompanyAccessManagement.Application.Common.Audit;

/// <summary>
/// One OperationId per request scope so every mutation in a single use case can be grouped (design §45).
/// </summary>
public interface IAuditOperation
{
    Guid OperationId { get; }
}

/// <summary>
/// Enqueues <see cref="AuditLog"/> rows on the current unit of work so they commit atomically with the mutation.
/// Never stores binary payload content.
/// </summary>
public interface IAuditWriter
{
    void Write(AuditWriteRequest request);
}

public sealed record AuditWriteRequest(
    string EventType,
    string EntityType,
    Guid EntityId,
    AccessRuleSourceType SourceType = AccessRuleSourceType.System,
    string? BeforeData = null,
    string? AfterData = null,
    string? Metadata = null,
    Guid? TargetPrincipalId = null,
    Guid? PermissionId = null,
    Guid? ApplicationId = null,
    Guid? CompanyId = null);
