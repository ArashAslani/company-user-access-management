using CleanArchitecture.Application.Common.Security;

namespace CleanArchitecture.Application.Common.Interfaces;

public interface IAccessEvaluator
{
    Task<AccessDecision> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default);
    Task EnsureAllowedAsync(AccessRequest request, CancellationToken cancellationToken = default);
}