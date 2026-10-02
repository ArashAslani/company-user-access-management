using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Common;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Infrastructure.Authorization;

public sealed class DelegationAuthority : IDelegationAuthority
{
    private readonly IApplicationDbContext _context;
    private readonly IAccessEvaluator _evaluator;
    private readonly TimeProvider _timeProvider;

    public DelegationAuthority(IApplicationDbContext context, IAccessEvaluator evaluator, TimeProvider timeProvider)
    {
        _context = context;
        _evaluator = evaluator;
        _timeProvider = timeProvider;
    }

    public async Task<DelegationAuthorityResult> AssessAsync(
        DelegationAssessmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var application = await _context.Applications
            .Where(a => a.Code == request.ApplicationCode && a.IsActive)
            .Select(a => new { a.Id })
            .FirstOrDefaultAsync(cancellationToken);
        if (application is null)
            throw new ForbiddenAccessException();

        var permissionId = await (from permission in _context.Permissions
                                  join resource in _context.Resources on permission.ResourceId equals resource.Id
                                  where resource.ApplicationId == application.Id
                                      && resource.Code + "." + permission.ActionCode == request.PermissionCode
                                  select (Guid?)permission.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (permissionId is null)
            throw new ForbiddenAccessException();

        var membership = await _context.UserCompanies
            .AsNoTracking()
            .Include(uc => uc.Roles)
            .FirstOrDefaultAsync(uc => uc.UserId == request.DelegatorUserId
                && uc.CompanyId == request.CompanyId
                && uc.Status == UserCompanyStatus.Active, cancellationToken);
        if (membership is null)
            throw new ForbiddenAccessException();

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var roles = await _context.Roles.AsNoTracking()
            .Where(r => r.CompanyId == request.CompanyId && r.ApplicationId == application.Id)
            .Select(r => new RoleNode(r.Id, r.ParentRoleId, r.Kind, r.Status, r.ValidUntil))
            .ToListAsync(cancellationToken);
        var graph = new RoleGraph(roles);
        var sourceRoleIds = membership.Roles
            .SelectMany(ur => graph.SelfAndDescendants(ur.RoleId))
            .Distinct()
            .ToHashSet();

        var rolePrincipals = await _context.AuthPrincipals.AsNoTracking()
            .Where(p => p.Type == PrincipalType.Role && p.CompanyId == request.CompanyId
                && p.ApplicationId == application.Id && sourceRoleIds.Contains(p.ReferenceId))
            .Select(p => new { p.Id, RoleId = p.ReferenceId })
            .ToListAsync(cancellationToken);
        var roleByPrincipal = rolePrincipals.ToDictionary(p => p.Id, p => p.RoleId);
        var principalIds = rolePrincipals.Select(p => p.Id).Append(membership.PrincipalId).ToList();

        var rules = await _context.AccessRules.AsNoTracking()
            .Include(r => r.Scopes)
            .Where(r => principalIds.Contains(r.PrincipalId)
                && r.PermissionId == permissionId.Value
                && r.Effect == AccessEffect.Allow
                && r.Origin != AccessRuleOrigin.Delegated
                && r.Status == AccessRuleStatus.Active
                && (r.ValidFrom == null || r.ValidFrom <= now)
                && (r.ValidUntil == null || r.ValidUntil > now))
            .ToListAsync(cancellationToken);

        rules = rules.Where(rule => !roleByPrincipal.TryGetValue(rule.PrincipalId, out var roleId)
            || roles.Any(r => r.Id == roleId && r.Status == RoleStatus.Active && (r.ValidUntil == null || r.ValidUntil > now)))
            .ToList();

        var checks = request.RequestedScopeMode == ScopeMode.Selected
            ? request.RequestedScopes.Select(s => new AccessRequest(request.DelegatorUserId, request.CompanyId,
                request.ApplicationCode, request.PermissionCode, s.ScopeType, s.ScopeKey)).ToList()
            : [new AccessRequest(request.DelegatorUserId, request.CompanyId, request.ApplicationCode, request.PermissionCode)];

        if (rules.Count == 0)
        {
            await EnsureOwnedAsync(checks, cancellationToken);
            throw new ForbiddenAccessException();
        }

        if (!IsScopeSubset(request, rules))
        {
            foreach (var accessRequest in checks)
                if (await IsHeldOnlyThroughDelegationAsync(accessRequest, cancellationToken))
                    throw RedelegationForbidden();

            throw new ValidationException([
                new ValidationFailure("ScopeKeys", "Requested delegation scope must be a subset of the delegator's non-delegated grant.")
            ]);
        }

        await EnsureOwnedAsync(checks, cancellationToken);

        var sourceBoundaries = rules
            .Where(r => ContributesToRequest(r, request))
            .Select(r => Earliest(r.ValidUntil,
                roleByPrincipal.TryGetValue(r.PrincipalId, out var roleId) ? roles.First(x => x.Id == roleId).ValidUntil : null))
            .ToList();

        if (request.RequestedValidUntil is DateTime requestedUntil
            && sourceBoundaries.Count > 0
            && sourceBoundaries.All(b => b.HasValue)
            && requestedUntil > sourceBoundaries.Max(b => b!.Value))
        {
            throw new DomainRuleViolationException(
                "DELEGATION_VALID_UNTIL_EXCEEDS_SOURCE",
                "Delegation validity cannot exceed the earliest validity boundary of its source grant.");
        }

        return new DelegationAuthorityResult(permissionId.Value, application.Id, membership.PrincipalId, request.RequestedValidUntil);
    }

    private async Task EnsureOwnedAsync(IReadOnlyList<AccessRequest> checks, CancellationToken cancellationToken)
    {
        foreach (var accessRequest in checks)
        {
            var owned = await _evaluator.EvaluateAsync(accessRequest, excludeDelegation: true, cancellationToken);
            if (owned.Allowed)
                continue;

            var delegated = await _evaluator.EvaluateAsync(accessRequest, excludeDelegation: false, cancellationToken);
            if (delegated.Allowed)
                throw RedelegationForbidden();

            throw new ForbiddenAccessException();
        }
    }

    private async Task<bool> IsHeldOnlyThroughDelegationAsync(AccessRequest accessRequest, CancellationToken cancellationToken)
        => !(await _evaluator.EvaluateAsync(accessRequest, excludeDelegation: true, cancellationToken)).Allowed
            && (await _evaluator.EvaluateAsync(accessRequest, excludeDelegation: false, cancellationToken)).Allowed;

    private static DomainRuleViolationException RedelegationForbidden()
        => new("REDELEGATION_FORBIDDEN", "A delegated permission cannot be delegated again.");

    private static DateTime? Earliest(DateTime? first, DateTime? second)
        => first is null ? second : second is null ? first : first < second ? first : second;

    private static bool IsScopeSubset(DelegationAssessmentRequest request, IReadOnlyCollection<AccessRule> rules)
        => request.RequestedScopeMode switch
        {
            ScopeMode.All => rules.Any(r => r.ScopeMode == ScopeMode.All),
            ScopeMode.None => rules.Any(r => r.ScopeMode is ScopeMode.None or ScopeMode.All),
            ScopeMode.Selected => rules.Any(r => r.ScopeMode == ScopeMode.All)
                || request.RequestedScopes.All(scope => rules.Any(r => r.ScopeMode == ScopeMode.Selected
                    && r.Scopes.Any(s => s.ScopeType == scope.ScopeType && s.ScopeKey == scope.ScopeKey))),
            _ => false
        };

    private static bool ContributesToRequest(AccessRule rule, DelegationAssessmentRequest request)
        => request.RequestedScopeMode switch
        {
            ScopeMode.All => rule.ScopeMode == ScopeMode.All,
            ScopeMode.None => rule.ScopeMode is ScopeMode.None or ScopeMode.All,
            ScopeMode.Selected => rule.ScopeMode == ScopeMode.All
                || request.RequestedScopes.Any(scope => rule.ScopeMode == ScopeMode.Selected
                    && rule.Scopes.Any(s => s.ScopeType == scope.ScopeType && s.ScopeKey == scope.ScopeKey)),
            _ => false
        };
}
