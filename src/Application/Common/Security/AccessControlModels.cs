namespace CleanArchitecture.Application.Common.Security;

public sealed record AccessRequest(
    Guid UserId,
    Guid CompanyId,
    string ApplicationCode,
    string PermissionCode,
    string? ScopeType = null,
    string? ScopeKey = null
);

public sealed record AccessDecision(
    bool Allowed,
    string? ReasonCode,
    IReadOnlyCollection<AccessSource> Sources
);

public sealed record AccessSource(
    string SourceType,
    string SourceId,
    string? ScopeMode = null,
    IReadOnlyCollection<string>? ScopeKeys = null
);