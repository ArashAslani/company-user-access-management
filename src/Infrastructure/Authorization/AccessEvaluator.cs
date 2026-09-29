using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Infrastructure.Authorization;

/// <summary>
/// Evaluates one permission request for a user in a company (ADR-0005).
/// <list type="bullet">
/// <item>A scope-matched direct user DENY of the permission or any prerequisite denies globally.</item>
/// <item>ALLOW candidates are direct user rules, role branches and valid delegations.</item>
/// <item>For a directly assigned role R, ALLOWs come from R and its descendants (Role Up); DENYs on the path from the
/// origin role to R and on every ancestor of R block that branch only (Role Down, branch-local DENY).</item>
/// <item>Prerequisites must not be denied on the candidate's path and must themselves be effectively allowed.</item>
/// </list>
/// </summary>
public class AccessEvaluator : IAccessEvaluator
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public AccessEvaluator(IApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<AccessDecision> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var applicationId = await _context.Applications
            .Where(a => a.Code == request.ApplicationCode && a.IsActive)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (applicationId is null)
            return Denied("DENIED_APPLICATION");

        var permissionId = await (from p in _context.Permissions
                                  join r in _context.Resources on p.ResourceId equals r.Id
                                  where r.ApplicationId == applicationId && r.Code + "." + p.ActionCode == request.PermissionCode
                                  select (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        if (permissionId is null)
            return Denied("DENIED_PERMISSION_NOT_FOUND");

        var globalSuperAdminRoleId = await FindGlobalSuperAdminRoleAsync(request.UserId, now, cancellationToken);
        if (globalSuperAdminRoleId is not null)
            return new AccessDecision(true, "ALLOWED_GLOBAL_SUPER_ADMIN", [new AccessSource("GlobalSuperAdmin", globalSuperAdminRoleId.Value.ToString(), "All")]);

        var session = await EvaluationSession.CreateAsync(_context, request, applicationId.Value, permissionId.Value, now, cancellationToken);

        var subject = await session.LoadSubjectAsync(request.UserId, cancellationToken);
        if (subject is null)
            return Denied("DENIED_MEMBERSHIP");

        var companySuperAdminRoleId = session.FindCompanySuperAdminRole(subject);
        if (companySuperAdminRoleId is not null)
            return new AccessDecision(true, "ALLOWED_COMPANY_SUPER_ADMIN", [new AccessSource("CompanySuperAdmin", companySuperAdminRoleId.Value.ToString(), "All")]);

        var outcome = await session.EvaluateAsync(subject, permissionId.Value, excludeDelegation: false, cancellationToken);
        return new AccessDecision(outcome.Allowed, outcome.ReasonCode, outcome.Sources);
    }

    public async Task EnsureAllowedAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        var decision = await EvaluateAsync(request, cancellationToken);
        if (!decision.Allowed)
            throw new ForbiddenAccessException();
    }

    private async Task<Guid?> FindGlobalSuperAdminRoleAsync(Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        return await (from uc in _context.UserCompanies
                      where uc.UserId == userId && uc.Status == UserCompanyStatus.Active
                      from ur in uc.Roles
                      join r in _context.Roles on ur.RoleId equals r.Id
                      where r.Kind == RoleKind.GlobalSuperAdmin && r.Status == RoleStatus.Active
                      select (Guid?)r.Id).FirstOrDefaultAsync(cancellationToken);
    }

    private static AccessDecision Denied(string reasonCode) => new(false, reasonCode, []);

    private sealed record Subject(Guid UserId, Guid PrincipalId, IReadOnlyCollection<Guid> RoleIds);

    private sealed record Outcome(bool Allowed, string ReasonCode, IReadOnlyCollection<AccessSource> Sources)
    {
        public static Outcome Deny(string reasonCode) => new(false, reasonCode, []);
    }

    /// <summary>
    /// Holds everything loaded for one request: the role graph, role principals, the prerequisite graph and the
    /// active, time-valid rules for the permission and its prerequisites. Results are cached per subject and permission.
    /// </summary>
    private sealed class EvaluationSession
    {
        private readonly IApplicationDbContext _context;
        private readonly AccessRequest _request;
        private readonly DateTime _now;
        private readonly RoleGraph _roles;
        private readonly Dictionary<Guid, Guid> _roleIdByPrincipal;
        private readonly ILookup<Guid, Guid> _principalsByRole;
        private readonly ILookup<Guid, Guid> _directPrerequisites;
        private readonly HashSet<Guid> _relevantPermissions;
        private readonly ILookup<Guid, AccessRule> _roleRulesByPrincipal;
        private readonly Dictionary<Guid, List<AccessRule>> _subjectRules = [];
        private readonly Dictionary<(Guid PrincipalId, Guid PermissionId, bool ExcludeDelegation), Outcome> _outcomes = [];

        private EvaluationSession(
            IApplicationDbContext context,
            AccessRequest request,
            DateTime now,
            RoleGraph roles,
            Dictionary<Guid, Guid> roleIdByPrincipal,
            ILookup<Guid, Guid> directPrerequisites,
            HashSet<Guid> relevantPermissions,
            IEnumerable<AccessRule> roleRules)
        {
            _context = context;
            _request = request;
            _now = now;
            _roles = roles;
            _roleIdByPrincipal = roleIdByPrincipal;
            _principalsByRole = roleIdByPrincipal.ToLookup(p => p.Value, p => p.Key);
            _directPrerequisites = directPrerequisites;
            _relevantPermissions = relevantPermissions;
            _roleRulesByPrincipal = roleRules.ToLookup(r => r.PrincipalId);
        }

        public static async Task<EvaluationSession> CreateAsync(
            IApplicationDbContext context, AccessRequest request, Guid applicationId, Guid permissionId, DateTime now, CancellationToken cancellationToken)
        {
            var roles = await context.Roles
                .AsNoTracking()
                .Where(r => r.CompanyId == request.CompanyId && r.ApplicationId == applicationId)
                .Select(r => new RoleNode(r.Id, r.ParentRoleId, r.Kind, r.Status, r.ValidUntil))
                .ToListAsync(cancellationToken);

            var roleIdByPrincipal = await context.AuthPrincipals
                .AsNoTracking()
                .Where(ap => ap.Type == PrincipalType.Role && ap.CompanyId == request.CompanyId && ap.ApplicationId == applicationId)
                .ToDictionaryAsync(ap => ap.Id, ap => ap.ReferenceId, cancellationToken);

            var implications = await (from i in context.PermissionImplications
                                      join p in context.Permissions on i.PermissionId equals p.Id
                                      join r in context.Resources on p.ResourceId equals r.Id
                                      where r.ApplicationId == applicationId
                                      select new { i.PermissionId, i.RequiredPermissionId }).ToListAsync(cancellationToken);
            var directPrerequisites = implications.ToLookup(i => i.PermissionId, i => i.RequiredPermissionId);

            var relevantPermissions = TransitivePrerequisites(directPrerequisites, permissionId);
            relevantPermissions.Add(permissionId);

            var rolePrincipalIds = roleIdByPrincipal.Keys.ToList();
            var roleRules = await ActiveRules(context, now)
                .Where(ar => rolePrincipalIds.Contains(ar.PrincipalId)
                    && relevantPermissions.Contains(ar.PermissionId)
                    && ar.Origin != AccessRuleOrigin.Delegated)
                .ToListAsync(cancellationToken);

            return new EvaluationSession(
                context, request, now, new RoleGraph(roles), roleIdByPrincipal, directPrerequisites, relevantPermissions, roleRules);
        }

        public async Task<Subject?> LoadSubjectAsync(Guid userId, CancellationToken cancellationToken)
        {
            var membership = await _context.UserCompanies
                .AsNoTracking()
                .Include(uc => uc.Roles)
                .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.CompanyId == _request.CompanyId && uc.Status == UserCompanyStatus.Active, cancellationToken);

            return membership is null
                ? null
                : new Subject(membership.UserId, membership.PrincipalId, membership.Roles.Select(r => r.RoleId).ToList());
        }

        public Guid? FindCompanySuperAdminRole(Subject subject)
        {
            foreach (var roleId in subject.RoleIds)
            {
                if (_roles.TryGet(roleId, out var role) && role.Kind == RoleKind.CompanySuperAdmin && role.Status == RoleStatus.Active)
                    return role.Id;
            }

            return null;
        }

        public Task<Outcome> EvaluateAsync(Subject subject, Guid permissionId, bool excludeDelegation, CancellationToken cancellationToken)
            => EvaluateAsync(subject, permissionId, excludeDelegation, [], cancellationToken);

        private async Task<Outcome> EvaluateAsync(Subject subject, Guid permissionId, bool excludeDelegation, HashSet<Guid> inProgress, CancellationToken cancellationToken)
        {
            var key = (subject.PrincipalId, permissionId, excludeDelegation);
            if (_outcomes.TryGetValue(key, out var cached))
                return cached;

            // A prerequisite cycle can never be satisfied.
            if (!inProgress.Add(permissionId))
                return Outcome.Deny("DENIED_PREREQUISITE_GATE");

            try
            {
                var outcome = await EvaluateUncachedAsync(subject, permissionId, excludeDelegation, inProgress, cancellationToken);
                _outcomes[key] = outcome;
                return outcome;
            }
            finally
            {
                inProgress.Remove(permissionId);
            }
        }

        private async Task<Outcome> EvaluateUncachedAsync(Subject subject, Guid permissionId, bool excludeDelegation, HashSet<Guid> inProgress, CancellationToken cancellationToken)
        {
            var prerequisites = TransitivePrerequisites(_directPrerequisites, permissionId);
            var directRules = await GetSubjectRulesAsync(subject.PrincipalId, cancellationToken);

            var directDeny = directRules.Any(r => r.Effect == AccessEffect.Deny
                && r.Origin != AccessRuleOrigin.Delegated
                && (r.PermissionId == permissionId || prerequisites.Contains(r.PermissionId))
                && ScopeMatches(r));
            if (directDeny)
                return Outcome.Deny("DENIED_EXPLICIT_DENY");

            var candidates = new List<RuleCandidate>();

            foreach (var rule in directRules.Where(r => IsAllowFor(r, permissionId) && r.Origin != AccessRuleOrigin.Delegated))
                candidates.Add(new RuleCandidate(rule, CandidateSource.DirectUser, null, null, new HashSet<Guid>()));

            foreach (var branchRoleId in subject.RoleIds.Where(id => _roles.TryGet(id, out _)))
            {
                var ancestors = _roles.Ancestors(branchRoleId);

                foreach (var originRoleId in _roles.SelfAndDescendants(branchRoleId))
                {
                    _roles.TryGet(originRoleId, out var origin);
                    if (!CanGrant(origin))
                        continue;

                    foreach (var principalId in _principalsByRole[originRoleId])
                    {
                        foreach (var rule in _roleRulesByPrincipal[principalId].Where(r => IsAllowFor(r, permissionId)))
                        {
                            var path = new HashSet<Guid>(_roles.PathUpTo(originRoleId, branchRoleId));
                            path.UnionWith(ancestors);
                            candidates.Add(new RuleCandidate(rule, CandidateSource.RoleBranch, branchRoleId, originRoleId, path));
                        }
                    }
                }
            }

            if (!excludeDelegation)
            {
                foreach (var rule in directRules.Where(r => IsAllowFor(r, permissionId) && r.Origin == AccessRuleOrigin.Delegated))
                {
                    if (await IsDelegationSourceValidAsync(rule, permissionId, cancellationToken))
                        candidates.Add(new RuleCandidate(rule, CandidateSource.Delegation, null, null, new HashSet<Guid>()));
                }
            }

            if (candidates.Count == 0)
                return Outcome.Deny("DENIED_NO_PERMISSION");

            var passedDenyGate = false;
            var passing = new List<RuleCandidate>();
            foreach (var candidate in candidates)
            {
                if (HasRoleDeny(candidate.PathRoleIds, p => p == permissionId))
                    continue;

                passedDenyGate = true;

                if (HasRoleDeny(candidate.PathRoleIds, prerequisites.Contains))
                    continue;

                passing.Add(candidate);
            }

            if (passing.Count > 0)
            {
                foreach (var prerequisiteId in _directPrerequisites[permissionId])
                {
                    var prerequisite = await EvaluateAsync(subject, prerequisiteId, excludeDelegation, inProgress, cancellationToken);
                    if (!prerequisite.Allowed)
                    {
                        passing.Clear();
                        break;
                    }
                }
            }

            if (passing.Count > 0)
                return new Outcome(true, "ALLOWED", passing.Select(ToSource).ToList());

            return Outcome.Deny(passedDenyGate ? "DENIED_PREREQUISITE_GATE" : "DENIED_EXPLICIT_DENY");
        }

        private async Task<bool> IsDelegationSourceValidAsync(AccessRule delegation, Guid permissionId, CancellationToken cancellationToken)
        {
            if (delegation.DelegatedFromUserId is not Guid delegatorUserId)
                return false;

            var delegator = await LoadSubjectAsync(delegatorUserId, cancellationToken);
            if (delegator is null)
                return false;

            var source = await EvaluateAsync(delegator, permissionId, excludeDelegation: true, cancellationToken);
            return source.Allowed;
        }

        private static bool CanGrant(RoleNode role) => true;

        private bool HasRoleDeny(IReadOnlySet<Guid> pathRoleIds, Func<Guid, bool> permissionMatches)
        {
            foreach (var roleId in pathRoleIds)
            {
                foreach (var principalId in _principalsByRole[roleId])
                {
                    if (_roleRulesByPrincipal[principalId].Any(r => r.Effect == AccessEffect.Deny && permissionMatches(r.PermissionId) && ScopeMatches(r)))
                        return true;
                }
            }

            return false;
        }

        private bool IsAllowFor(AccessRule rule, Guid permissionId)
            => rule.Effect == AccessEffect.Allow && rule.PermissionId == permissionId && ScopeMatches(rule);

        private bool ScopeMatches(AccessRule rule) => rule.ScopeMode switch
        {
            ScopeMode.None => _request.ScopeType is null,
            ScopeMode.All => true,
            ScopeMode.Selected => _request.ScopeType is not null
                && _request.ScopeKey is not null
                && rule.Scopes.Any(s => s.ScopeType == _request.ScopeType && s.ScopeKey == _request.ScopeKey),
            _ => false
        };

        private async Task<List<AccessRule>> GetSubjectRulesAsync(Guid principalId, CancellationToken cancellationToken)
        {
            if (!_subjectRules.TryGetValue(principalId, out var rules))
            {
                rules = await ActiveRules(_context, _now)
                    .Where(ar => ar.PrincipalId == principalId && _relevantPermissions.Contains(ar.PermissionId))
                    .ToListAsync(cancellationToken);
                _subjectRules[principalId] = rules;
            }

            return rules;
        }

        private static AccessSource ToSource(RuleCandidate candidate) => new(
            candidate.Source.ToString(),
            (candidate.OriginRoleId ?? candidate.Rule.PrincipalId).ToString(),
            candidate.Rule.ScopeMode.ToString(),
            candidate.Rule.Scopes.Select(s => $"{s.ScopeType}:{s.ScopeKey}").ToList());

        private static IQueryable<AccessRule> ActiveRules(IApplicationDbContext context, DateTime now)
            => context.AccessRules
                .AsNoTracking()
                .Include(ar => ar.Scopes)
                .Where(ar => ar.Status == AccessRuleStatus.Active
                    && (ar.ValidFrom == null || ar.ValidFrom <= now)
                    && (ar.ValidUntil == null || ar.ValidUntil > now));

        private static HashSet<Guid> TransitivePrerequisites(ILookup<Guid, Guid> directPrerequisites, Guid permissionId)
        {
            var result = new HashSet<Guid>();
            var queue = new Queue<Guid>(directPrerequisites[permissionId]);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == permissionId || !result.Add(current))
                    continue;

                foreach (var next in directPrerequisites[current])
                    queue.Enqueue(next);
            }

            return result;
        }
    }
}
