using CompanyAccessManagement.Application.Common.Security;

namespace CompanyAccessManagement.Application.Common.Interfaces;

public interface IAccessEvaluator
{
    Task<AccessDecision> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default);

    /// <param name="excludeDelegation">When true, only direct and role-branch grants count (same path as delegation source validation).</param>
    Task<AccessDecision> EvaluateAsync(AccessRequest request, bool excludeDelegation, CancellationToken cancellationToken = default);

    Task EnsureAllowedAsync(AccessRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Keys of <see cref="EffectiveScopesRequest.ScopeType"/> for which <see cref="EvaluateAsync"/> would allow
    /// <see cref="EffectiveScopesRequest.PermissionCode"/>. All means every key; Selected with an empty list means none.
    /// </summary>
    Task<EffectiveScopesResult> EvaluateEffectiveScopesAsync(EffectiveScopesRequest request, CancellationToken cancellationToken = default);
}
