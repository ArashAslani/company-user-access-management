namespace CompanyAccessManagement.Application.AccessControl.Audit.Queries;

public record AuditLogDto
{
    public Guid OperationId { get; init; }
    public Guid ActorUserId { get; init; }
    public string ActorFullName { get; init; } = null!;
    public string ActorRoleTitle { get; init; } = null!;
    public DateTimeOffset OccurredAt { get; init; }
    public string ChangeType { get; init; } = null!;
    public string Source { get; init; } = null!;
    public string ResourceSummary { get; init; } = null!;
}

public record AuditLogDetailDto
{
    public Guid ActorUserId { get; init; }
    public string ActorFullName { get; init; } = null!;
    public string ActorRoleTitle { get; init; } = null!;
    public DateTimeOffset OccurredAt { get; init; }
    public string ChangeType { get; init; } = null!;
    public string Source { get; init; } = null!;
    public List<ChangedModuleDto> ChangedModules { get; init; } = new();
}

public record ChangedModuleDto
{
    public Guid ResourceId { get; init; }
    public string ResourceName { get; init; } = null!;
    public string SubResourceName { get; init; } = null!;
    public List<ChangedActionDto> Actions { get; init; } = new();
}

public record ChangedActionDto
{
    public string ActionCode { get; init; } = null!;
    public bool Before { get; init; }
    public bool After { get; init; }
}
