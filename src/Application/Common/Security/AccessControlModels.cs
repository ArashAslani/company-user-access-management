namespace CompanyAccessManagement.Application.Common.Security;

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

public sealed record EffectiveScopesRequest(
    Guid UserId,
    Guid CompanyId,
    string ApplicationCode,
    string PermissionCode,
    string ScopeType
);

/// <summary>
/// <paramref name="Keys"/> is empty when <paramref name="ScopeMode"/> is All, and when the caller has no keys.
/// <paramref name="Revision"/> is the ADR-0007 token <c>membership.company.policy</c> (or <c>0.0.0</c> without a membership).
/// </summary>
public sealed record EffectiveScopesResult(
    Domain.AccessControl.ScopeMode ScopeMode,
    string ScopeType,
    IReadOnlyList<string> Keys,
    string Revision
);
