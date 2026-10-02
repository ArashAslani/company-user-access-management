using CompanyAccessManagement.Domain.AccessControl;

namespace CompanyAccessManagement.Application.Common.Interfaces;

/// <summary>
/// Validates that the current user may create a delegation of a permission (design §33): real non-delegated ownership
/// via the same evaluation path as runtime source checks, scope subsetting and ValidUntil capping.
/// </summary>
public interface IDelegationAuthority
{
    Task<DelegationAuthorityResult> AssessAsync(DelegationAssessmentRequest request, CancellationToken cancellationToken = default);
}

public sealed record DelegationAssessmentRequest(
    Guid DelegatorUserId,
    Guid CompanyId,
    string ApplicationCode,
    string PermissionCode,
    ScopeMode RequestedScopeMode,
    IReadOnlyList<DelegationScopeRequest> RequestedScopes,
    DateTime? RequestedValidUntil);

public sealed record DelegationScopeRequest(string ScopeType, string ScopeKey);

public sealed record DelegationAuthorityResult(
    Guid PermissionId,
    Guid ApplicationId,
    Guid DelegatorPrincipalId,
    DateTime? CappedValidUntil);
