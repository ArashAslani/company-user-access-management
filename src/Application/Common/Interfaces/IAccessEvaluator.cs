using CompanyAccessManagement.Application.Common.Security;

namespace CompanyAccessManagement.Application.Common.Interfaces;

public interface IAccessEvaluator
{
    Task<AccessDecision> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default);
    Task EnsureAllowedAsync(AccessRequest request, CancellationToken cancellationToken = default);
}
