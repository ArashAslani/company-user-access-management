using CompanyAccessManagement.Domain.AccessControl;

namespace CompanyAccessManagement.Infrastructure.Authorization;

internal enum CandidateSource { DirectUser, RoleBranch, Delegation }

/// <summary>
/// A scope-matched ALLOW rule that could grant the requested permission.
/// For role branches, <see cref="PathRoleIds"/> is the path from the origin role up to the directly assigned
/// branch role plus every ancestor of that branch role; a DENY on any of them blocks this candidate only.
/// </summary>
internal sealed record RuleCandidate(
    AccessRule Rule,
    CandidateSource Source,
    Guid? BranchRoleId,
    Guid? OriginRoleId,
    IReadOnlySet<Guid> PathRoleIds);
