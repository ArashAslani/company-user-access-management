using CompanyAccessManagement.Application.Common.Security;

namespace CompanyAccessManagement.Application.Common.Interfaces;

public interface IAccessEvaluator
{
    Task<AccessDecision> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default);

    /// <param name="excludeDelegation">When true, only direct and role-branch grants count (same path as delegation source validation).</param>
    Task<AccessDecision> EvaluateAsync(AccessRequest request, bool excludeDelegation, CancellationToken cancellationToken = default);

    Task EnsureAllowedAsync(AccessRequest request, CancellationToken cancellationToken = default);
}
