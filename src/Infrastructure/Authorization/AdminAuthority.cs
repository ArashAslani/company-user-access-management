using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Infrastructure.Authorization;

/// <summary>
/// Admin Authority (design §32, ADR-0005) evaluated with the same rules as <see cref="AccessEvaluator"/>. Each directly held,
/// active standard role M of which the target is a strict descendant is tried on its own: every grant must be effectively
/// allowed by M's branch alone (Role Up, DENY boundaries, prerequisites, the actor's direct DENY) within the requested scope.
/// </summary>
public class AdminAuthority : IAdminAuthority
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _user;
    private readonly ICurrentWorkspace _workspace;
    private readonly TimeProvider _timeProvider;

    public AdminAuthority(IApplicationDbContext context, IUser user, ICurrentWorkspace workspace, TimeProvider timeProvider)
    {
        _context = context;
        _user = user;
        _workspace = workspace;
        _timeProvider = timeProvider;
    }

    public async Task EnsureCanGrantAsync(Guid targetRoleId, IReadOnlyCollection<AuthorityGrant> grants, CancellationToken cancellationToken)
    {
        if (!await HasAuthorityAsync(targetRoleId, grants, cancellationToken))
            throw new ForbiddenAccessException();
    }

    public Task EnsureCanAssignAsync(Guid roleId, CancellationToken cancellationToken)
        => EnsureCanGrantAsync(roleId, [], cancellationToken);

    private async Task<bool> HasAuthorityAsync(Guid targetRoleId, IReadOnlyCollection<AuthorityGrant> grants, CancellationToken cancellationToken)
    {
        if (_user.Id is not Guid userId || _workspace.CompanyId is not Guid companyId)
            return false;

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var applicationId = await _context.Applications
            .Where(a => a.Code == AccessControlApplication.Code && a.IsActive)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (applicationId is null)
            return false;

        if (await AccessEvaluator.FindGlobalSuperAdminRoleAsync(_context, userId, now, cancellationToken) is not null)
            return true;

        var baseRequest = new AccessRequest(userId, companyId, AccessControlApplication.Code, string.Empty);
        var structure = await AccessEvaluator.EvaluationSession.CreateAsync(_context, baseRequest, applicationId.Value, Guid.Empty, now, cancellationToken);

        var subject = await structure.LoadSubjectAsync(userId, cancellationToken);
        if (subject is null)
            return false;

        if (structure.FindCompanySuperAdminRole(subject) is not null)
            return true;

        foreach (var managingRoleId in structure.ManagingRolesFor(subject, targetRoleId).ToList())
        {
            if (await CoversAsync(subject with { ManagingRoleId = managingRoleId }, baseRequest, applicationId.Value, grants, now, cancellationToken))
                return true;
        }

        return false;
    }

    private async Task<bool> CoversAsync(
        AccessEvaluator.Subject managed, AccessRequest baseRequest, Guid applicationId, IReadOnlyCollection<AuthorityGrant> grants, DateTime now, CancellationToken cancellationToken)
    {
        foreach (var grant in grants)
        {
            foreach (var (request, requireAll) in ScopeChecks(baseRequest, grant))
            {
                var session = await AccessEvaluator.EvaluationSession.CreateAsync(
                    _context, request, applicationId, grant.PermissionId, now, cancellationToken, requireAll);

                var outcome = await session.EvaluateAsync(managed, grant.PermissionId, excludeDelegation: true, cancellationToken);
                if (!outcome.Allowed)
                    return false;
            }
        }

        return true;
    }

    private static IEnumerable<(AccessRequest Request, bool RequireAll)> ScopeChecks(AccessRequest baseRequest, AuthorityGrant grant)
    {
        switch (grant.ScopeMode)
        {
            case ScopeMode.All:
                yield return (baseRequest, true);
                break;
            case ScopeMode.Selected:
                var scopes = grant.Scopes?.Distinct().ToList() ?? [];
                // A Selected grant without scopes is only covered by an All scope.
                if (scopes.Count == 0)
                {
                    yield return (baseRequest, true);
                    break;
                }
                foreach (var scope in scopes)
                    yield return (baseRequest with { ScopeType = scope.ScopeType, ScopeKey = scope.ScopeKey }, false);
                break;
            default:
                yield return (baseRequest, false);
                break;
        }
    }
}
