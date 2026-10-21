using System.Globalization;
using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CompanyAccessManagement.Infrastructure.Authorization;

/// <summary>
/// Evaluates one permission request for a user in a company (ADR-0005).
/// <list type="bullet">
/// <item>A scope-matched direct user DENY of the permission or any prerequisite denies globally.</item>
/// <item>ALLOW candidates are direct user rules, role branches and valid delegations.</item>
/// <item>For a directly assigned role R, ALLOWs come from R and its descendants (Role Up); DENYs on the path from the
/// origin role to R and on every ancestor of R block that branch only (Role Down, branch-local DENY).</item>
/// <item>Prerequisites must not be denied on the candidate's path and must themselves be effectively allowed.</item>
/// <item>Inactive or expired roles (including super-admin roles) grant nothing; a delegation is valid only while the delegator,
/// as an active member of the request company, is allowed the permission without counting delegations.</item>
/// </list>
/// Decisions for active members are cached (ADR-0007) under a key that includes the membership, company and application
/// revisions, and expire at the earliest role or rule validity boundary involved, or after <see cref="MaxCacheLifetime"/>.
public class AccessEvaluator : IAccessEvaluator
{
    public static readonly TimeSpan MaxCacheLifetime = TimeSpan.FromMinutes(5);

    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly IMemoryCache _cache;
    private readonly int _maxScopeKeys;

    public AccessEvaluator(IApplicationDbContext context, TimeProvider timeProvider, IMemoryCache cache, IOptions<EffectiveScopesOptions> options)
    {
        _context = context;
        _timeProvider = timeProvider;
        _cache = cache;
        _maxScopeKeys = options.Value.MaxKeys > 0 ? options.Value.MaxKeys : EffectiveScopesOptions.DefaultMaxKeys;
    }

    public async Task<AccessDecision> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default)
        => await EvaluateAsync(request, excludeDelegation: false, cancellationToken);

    public async Task<AccessDecision> EvaluateAsync(AccessRequest request, bool excludeDelegation, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var application = await _context.Applications
            .Where(a => a.Code == request.ApplicationCode && a.IsActive)
            .Select(a => new { a.Id, a.PolicyRevision })
            .FirstOrDefaultAsync(cancellationToken);
        if (application is null)
            return Denied("DENIED_APPLICATION");
        var applicationId = application.Id;

        var permissionId = await (from p in _context.Permissions
                                  join r in _context.Resources on p.ResourceId equals r.Id
                                  where r.ApplicationId == applicationId && r.Code + "." + p.ActionCode == request.PermissionCode
                                  select (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        if (permissionId is null)
            return Denied("DENIED_PERMISSION_NOT_FOUND");

        var globalSuperAdminRoleId = await FindGlobalSuperAdminRoleAsync(_context, request.UserId, now, cancellationToken);
        if (globalSuperAdminRoleId is not null)
            return new AccessDecision(true, "ALLOWED_GLOBAL_SUPER_ADMIN", [new AccessSource("GlobalSuperAdmin", globalSuperAdminRoleId.Value.ToString(), "All")]);

        // Revisions are read before any evaluation state: a mutation committed after this point changes the key,
        // so a decision computed from newer state is only ever stored under a key that is no longer requested.
        var revisions = await (from uc in _context.UserCompanies
                               join c in _context.Companies on uc.CompanyId equals c.Id
                               where uc.UserId == request.UserId && uc.CompanyId == request.CompanyId && uc.Status == UserCompanyStatus.Active
                               select new { Membership = uc.AuthorizationRevision, Company = c.AuthorizationRevision })
                              .FirstOrDefaultAsync(cancellationToken);
        if (revisions is null)
            return Denied("DENIED_MEMBERSHIP");

        var key = new DecisionCacheKey(request.UserId, request.CompanyId, request.ApplicationCode, request.PermissionCode,
            request.ScopeType, request.ScopeKey, revisions.Membership, revisions.Company, application.PolicyRevision);
        if (!excludeDelegation && _cache.TryGetValue(key, out CachedDecision? cached) && cached!.ExpiresAt > now)
            return cached.Decision;

        var session = await EvaluationSession.CreateAsync(_context, request, applicationId, permissionId.Value, now, cancellationToken);

        var subject = await session.LoadSubjectAsync(request.UserId, cancellationToken);
        if (subject is null)
            return Denied("DENIED_MEMBERSHIP");

        AccessDecision decision;
        var companySuperAdminRoleId = session.FindCompanySuperAdminRole(subject);
        if (companySuperAdminRoleId is not null)
        {
            decision = new AccessDecision(true, "ALLOWED_COMPANY_SUPER_ADMIN", [new AccessSource("CompanySuperAdmin", companySuperAdminRoleId.Value.ToString(), "All")]);
        }
        else
        {
            var outcome = await session.EvaluateAsync(subject, permissionId.Value, excludeDelegation, cancellationToken);
            decision = new AccessDecision(outcome.Allowed, outcome.ReasonCode, outcome.Sources);
        }

        if (!excludeDelegation)
        {
            var expiresAt = now + MaxCacheLifetime;
            if (await session.EarliestValidityBoundaryAsync(cancellationToken) is DateTime boundary && boundary < expiresAt)
                expiresAt = boundary;
            _cache.Set(key, new CachedDecision(decision, expiresAt), expiresAt - now);
        }

        return decision;
    }

    private sealed record DecisionCacheKey(
        Guid UserId, Guid CompanyId, string ApplicationCode, string PermissionCode, string? ScopeType, string? ScopeKey,
        long MembershipRevision, long CompanyRevision, long PolicyRevision);

    /// <param name="ExpiresAt">Checked against <see cref="TimeProvider"/> on every read; the cache's own expiry is only a backstop.</param>
    private sealed record CachedDecision(AccessDecision Decision, DateTime ExpiresAt);

    private sealed record EffectiveScopesCacheKey(
        Guid UserId, Guid CompanyId, string ApplicationCode, string PermissionCode, string ScopeType,
        long MembershipRevision, long CompanyRevision, long PolicyRevision);

    private sealed record CachedScopes(EffectiveScopesResult Result, DateTime ExpiresAt);

    public async Task<EffectiveScopesResult> EvaluateEffectiveScopesAsync(EffectiveScopesRequest request, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var application = await _context.Applications
            .Where(a => a.Code == request.ApplicationCode && a.IsActive)
            .Select(a => new { a.Id, a.PolicyRevision })
            .FirstOrDefaultAsync(cancellationToken);
        if (application is null)
            return EmptyScopes(request.ScopeType);

        var permissionId = await (from p in _context.Permissions
                                  join r in _context.Resources on p.ResourceId equals r.Id
                                  where r.ApplicationId == application.Id && r.Code + "." + p.ActionCode == request.PermissionCode
                                  select (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        if (permissionId is null)
            return EmptyScopes(request.ScopeType);

        var globalSuperAdminRevision = await FindGlobalSuperAdminCompanyRevisionAsync(_context, request.UserId, now, cancellationToken);
        if (globalSuperAdminRevision is not null)
        {
            var gsaRevision = await RevisionTokenAsync(request.UserId, request.CompanyId, application.PolicyRevision, cancellationToken);
            return new EffectiveScopesResult(ScopeMode.All, request.ScopeType, [], $"gsa.{globalSuperAdminRevision.Value.ToString(CultureInfo.InvariantCulture)}.{gsaRevision}");
        }

        var revisions = await ReadRevisionsAsync(request.UserId, request.CompanyId, cancellationToken);
        if (revisions is null)
            return EmptyScopes(request.ScopeType);

        var revision = FormatRevision(revisions.Value.Membership, revisions.Value.Company, application.PolicyRevision);
        var key = new EffectiveScopesCacheKey(request.UserId, request.CompanyId, request.ApplicationCode, request.PermissionCode,
            request.ScopeType, revisions.Value.Membership, revisions.Value.Company, application.PolicyRevision);
        if (_cache.TryGetValue(key, out CachedScopes? cached) && cached!.ExpiresAt > now)
            return cached.Result;

        var accessRequest = new AccessRequest(request.UserId, request.CompanyId, request.ApplicationCode, request.PermissionCode, request.ScopeType);
        var session = await EvaluationSession.CreateAsync(_context, accessRequest, application.Id, permissionId.Value, now, cancellationToken);
        var subject = await session.LoadSubjectAsync(request.UserId, cancellationToken);
        if (subject is null)
            return EmptyScopes(request.ScopeType, revision);

        var coverage = session.FindCompanySuperAdminRole(subject) is not null
            ? ScopeContribution.All
            : await session.EffectiveScopesAsync(subject, permissionId.Value, request.ScopeType, excludeDelegation: false, cancellationToken);

        var result = ToResult(coverage, request.ScopeType, revision);

        var expiresAt = now + MaxCacheLifetime;
        if (await session.EarliestValidityBoundaryAsync(cancellationToken) is DateTime boundary && boundary < expiresAt)
            expiresAt = boundary;
        _cache.Set(key, new CachedScopes(result, expiresAt), expiresAt - now);
        return result;
    }

    private EffectiveScopesResult ToResult(ScopeContribution coverage, string scopeType, string revision)
    {
        if (coverage.IsAll)
            return new EffectiveScopesResult(ScopeMode.All, scopeType, [], revision);
        if (coverage.IsUnrepresentable || coverage.KeyCount > _maxScopeKeys)
            throw new EffectiveScopesTooLargeException(_maxScopeKeys);

        return new EffectiveScopesResult(ScopeMode.Selected, scopeType, coverage.OrderedKeys(), revision);
    }

    private static EffectiveScopesResult EmptyScopes(string scopeType, string revision = "0.0.0")
        => new(ScopeMode.Selected, scopeType, [], revision);

    private async Task<(long Membership, long Company)?> ReadRevisionsAsync(Guid userId, Guid companyId, CancellationToken cancellationToken)
    {
        var revisions = await (from uc in _context.UserCompanies
                               join c in _context.Companies on uc.CompanyId equals c.Id
                               where uc.UserId == userId && uc.CompanyId == companyId && uc.Status == UserCompanyStatus.Active
                               select new { Membership = uc.AuthorizationRevision, Company = c.AuthorizationRevision })
                              .FirstOrDefaultAsync(cancellationToken);
        return revisions is null ? null : (revisions.Membership, revisions.Company);
    }

    private async Task<string> RevisionTokenAsync(Guid userId, Guid companyId, long policyRevision, CancellationToken cancellationToken)
    {
        var revisions = await ReadRevisionsAsync(userId, companyId, cancellationToken);
        return revisions is null
            ? FormatRevision(0, 0, policyRevision)
            : FormatRevision(revisions.Value.Membership, revisions.Value.Company, policyRevision);
    }

    private static string FormatRevision(long membership, long company, long policy)
        => string.Create(CultureInfo.InvariantCulture, $"{membership}.{company}.{policy}");

    /// <summary>Root-company authorization revision of an active GlobalSuperAdmin role, if the user holds one.</summary>
    private static async Task<long?> FindGlobalSuperAdminCompanyRevisionAsync(IApplicationDbContext context, Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        return await (from uc in context.UserCompanies
                      where uc.UserId == userId && uc.Status == UserCompanyStatus.Active
                      from ur in uc.Roles
                      join r in context.Roles on ur.RoleId equals r.Id
                      join c in context.Companies on r.CompanyId equals c.Id
                      where r.Kind == RoleKind.GlobalSuperAdmin && r.Status == RoleStatus.Active
                          && (r.ValidUntil == null || r.ValidUntil > now)
                          && c.ParentCompanyId == null
                      select (long?)c.AuthorizationRevision).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task EnsureAllowedAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        var decision = await EvaluateAsync(request, cancellationToken);
        if (!decision.Allowed)
            throw new ForbiddenAccessException();
    }

    /// <summary>A GlobalSuperAdmin role grants cross-company access only when it belongs to a root company (ADR-0006).</summary>
    internal static async Task<Guid?> FindGlobalSuperAdminRoleAsync(IApplicationDbContext context, Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        return await (from uc in context.UserCompanies
                      where uc.UserId == userId && uc.Status == UserCompanyStatus.Active
                      from ur in uc.Roles
                      join r in context.Roles on ur.RoleId equals r.Id
                      join c in context.Companies on r.CompanyId equals c.Id
                      where r.Kind == RoleKind.GlobalSuperAdmin && r.Status == RoleStatus.Active
                          && (r.ValidUntil == null || r.ValidUntil > now)
                          && c.ParentCompanyId == null
                      select (Guid?)r.Id).FirstOrDefaultAsync(cancellationToken);
    }

    private static AccessDecision Denied(string reasonCode) => new(false, reasonCode, []);

    /// <param name="ManagingRoleId">When set, only this directly held role's branch may grant (single-role admin authority);
    /// the user's direct DENY rules still apply.</param>
    internal sealed record Subject(Guid UserId, Guid PrincipalId, IReadOnlyCollection<Guid> RoleIds, Guid? ManagingRoleId = null);

    internal sealed record Outcome(bool Allowed, string ReasonCode, IReadOnlyCollection<AccessSource> Sources)
    {
        public static Outcome Deny(string reasonCode) => new(false, reasonCode, []);
    }

    /// <summary>
    /// Holds everything loaded for one request: the role graph, role principals, the prerequisite graph and the
    /// active, time-valid rules for the permission and its prerequisites. Results are cached per subject and permission.
    /// </summary>
    internal sealed class EvaluationSession
    {
        private readonly IApplicationDbContext _context;
        private readonly AccessRequest _request;
        private readonly DateTime _now;
        private readonly bool _requireAllScope;
        private readonly RoleGraph _roles;
        private readonly Dictionary<Guid, Guid> _roleIdByPrincipal;
        private readonly ILookup<Guid, Guid> _principalsByRole;
        private readonly ILookup<Guid, Guid> _directPrerequisites;
        private readonly HashSet<Guid> _relevantPermissions;
        private readonly ILookup<Guid, AccessRule> _roleRulesByPrincipal;
        private readonly Dictionary<Guid, List<AccessRule>> _subjectRules = [];
        private readonly Dictionary<(Guid PrincipalId, Guid? ManagingRoleId, Guid PermissionId, bool ExcludeDelegation), Outcome> _outcomes = [];
        private readonly Dictionary<(Guid PrincipalId, Guid? ManagingRoleId, Guid PermissionId, bool ExcludeDelegation, string ScopeType), ScopeContribution> _scopeOutcomes = [];

        private EvaluationSession(
            IApplicationDbContext context,
            AccessRequest request,
            DateTime now,
            bool requireAllScope,
            RoleGraph roles,
            Dictionary<Guid, Guid> roleIdByPrincipal,
            ILookup<Guid, Guid> directPrerequisites,
            HashSet<Guid> relevantPermissions,
            IEnumerable<AccessRule> roleRules)
        {
            _context = context;
            _request = request;
            _now = now;
            _requireAllScope = requireAllScope;
            _roles = roles;
            _roleIdByPrincipal = roleIdByPrincipal;
            _principalsByRole = roleIdByPrincipal.ToLookup(p => p.Value, p => p.Key);
            _directPrerequisites = directPrerequisites;
            _relevantPermissions = relevantPermissions;
            _roleRulesByPrincipal = roleRules.ToLookup(r => r.PrincipalId);
        }

        /// <param name="requireAllScope">Only ScopeMode.All ALLOW rules match and any DENY rule matches: used to decide whether a
        /// branch holds a permission over every scope.</param>
        public static async Task<EvaluationSession> CreateAsync(
            IApplicationDbContext context, AccessRequest request, Guid applicationId, Guid permissionId, DateTime now, CancellationToken cancellationToken,
            bool requireAllScope = false)
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
                context, request, now, requireAllScope, new RoleGraph(roles), roleIdByPrincipal, directPrerequisites, relevantPermissions, roleRules);
        }

        /// <summary>
        /// Earliest future instant at which a role or rule that can affect this request changes validity: any role's
        /// ValidUntil in the company and application, and ValidFrom / ValidUntil of every active rule in the company on the
        /// permission or its prerequisites (direct, role, delegated and delegator rules alike). Null when nothing changes.
        /// </summary>
        public async Task<DateTime?> EarliestValidityBoundaryAsync(CancellationToken cancellationToken)
        {
            var now = _now;
            var boundaries = _roles.All
                .Where(r => r.ValidUntil > now)
                .Select(r => r.ValidUntil!.Value)
                .ToList();

            var windows = await (from ar in _context.AccessRules
                                 join ap in _context.AuthPrincipals on ar.PrincipalId equals ap.Id
                                 where ap.CompanyId == _request.CompanyId
                                     && ar.Status == AccessRuleStatus.Active
                                     && _relevantPermissions.Contains(ar.PermissionId)
                                     && ((ar.ValidFrom != null && ar.ValidFrom > now) || (ar.ValidUntil != null && ar.ValidUntil > now))
                                 select new { ar.ValidFrom, ar.ValidUntil }).ToListAsync(cancellationToken);

            foreach (var window in windows)
            {
                if (window.ValidFrom > now)
                    boundaries.Add(window.ValidFrom.Value);
                if (window.ValidUntil > now)
                    boundaries.Add(window.ValidUntil.Value);
            }

            return boundaries.Count == 0 ? null : boundaries.Min();
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
                if (_roles.TryGet(roleId, out var role) && role.Kind == RoleKind.CompanySuperAdmin && CanGrant(role))
                    return role.Id;
            }

            return null;
        }

        /// <summary>Directly held, grantable standard roles of which <paramref name="targetRoleId"/> is a strict descendant.</summary>
        public IEnumerable<Guid> ManagingRolesFor(Subject subject, Guid targetRoleId)
        {
            foreach (var roleId in subject.RoleIds.Distinct())
            {
                if (roleId != targetRoleId
                    && _roles.TryGet(roleId, out var role)
                    && role.Kind == RoleKind.Standard
                    && CanGrant(role)
                    && _roles.SelfAndDescendants(roleId).Contains(targetRoleId))
                    yield return roleId;
            }
        }

        public Task<Outcome> EvaluateAsync(Subject subject, Guid permissionId, bool excludeDelegation, CancellationToken cancellationToken)
            => EvaluateAsync(subject, permissionId, excludeDelegation, [], cancellationToken);

        private async Task<Outcome> EvaluateAsync(Subject subject, Guid permissionId, bool excludeDelegation, HashSet<Guid> inProgress, CancellationToken cancellationToken)
        {
            var key = (subject.PrincipalId, subject.ManagingRoleId, permissionId, excludeDelegation);
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

            var candidates = await CollectCandidatesAsync(subject, directRules, permissionId, excludeDelegation, matchRequestScope: true, validateDelegations: true, cancellationToken);

            if (candidates.Count == 0)
                return Outcome.Deny("DENIED_NO_PERMISSION");

            var passedDenyGate = false;
            var passing = new List<RuleCandidate>();
            var scoped = !_requireAllScope && _request.ScopeType is not null && _request.ScopeKey is not null;
            foreach (var candidate in candidates)
            {
                if (scoped)
                {
                    // Same candidate scope set the effective-scopes query unions. A key survives iff this returns it.
                    if (!CandidateScope(candidate, _request.ScopeType!, id => id == permissionId).Contains(_request.ScopeKey!))
                        continue;

                    passedDenyGate = true;

                    if (!CandidateScope(candidate, _request.ScopeType!, id => id == permissionId || prerequisites.Contains(id)).Contains(_request.ScopeKey!))
                        continue;
                }
                else
                {
                    if (HasRoleDeny(candidate.PathRoleIds, p => p == permissionId))
                        continue;

                    passedDenyGate = true;

                    if (HasRoleDeny(candidate.PathRoleIds, prerequisites.Contains))
                        continue;
                }

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

        /// <summary>Inactive or expired roles grant nothing, but their DENY rules still apply.</summary>
        private bool CanGrant(RoleNode role)
            => role.Status == RoleStatus.Active && (role.ValidUntil is null || role.ValidUntil > _now);

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

        private static bool IsAllowRule(AccessRule rule, Guid permissionId)
            => rule.Effect == AccessEffect.Allow && rule.PermissionId == permissionId;

        private bool ScopeMatches(AccessRule rule) => _requireAllScope
            ? rule.Effect == AccessEffect.Deny || rule.ScopeMode == ScopeMode.All
            : rule.ScopeMode switch
            {
                ScopeMode.None => _request.ScopeType is null,
                ScopeMode.All => true,
                ScopeMode.Selected => _request.ScopeType is not null
                    && _request.ScopeKey is not null
                    && ScopeContribution.FromRule(rule, _request.ScopeType).Contains(_request.ScopeKey),
                _ => false
            };

        /// <summary>
        /// Keys of <paramref name="scopeType"/> this ALLOW candidate still grants after path DENYs of
        /// <paramref name="denyPermissions"/> are removed. Point evaluation and the effective-scopes query both use it.
        /// </summary>
        private ScopeContribution CandidateScope(RuleCandidate candidate, string scopeType, Func<Guid, bool> denyPermissions)
        {
            var granted = ScopeContribution.FromRule(candidate.Rule, scopeType);
            if (granted.IsEmpty)
                return granted;

            return granted.Except(PathDenyContribution(candidate.PathRoleIds, denyPermissions, scopeType));
        }

        private ScopeContribution PathDenyContribution(IReadOnlySet<Guid> pathRoleIds, Func<Guid, bool> permissionMatches, string scopeType)
        {
            var denied = ScopeContribution.Empty;
            foreach (var roleId in pathRoleIds)
            {
                foreach (var principalId in _principalsByRole[roleId])
                {
                    foreach (var rule in _roleRulesByPrincipal[principalId])
                    {
                        if (rule.Effect == AccessEffect.Deny && permissionMatches(rule.PermissionId))
                            denied = denied.Union(ScopeContribution.FromRule(rule, scopeType));
                    }
                }
            }

            return denied;
        }

        private static ScopeContribution DirectDenyContribution(IEnumerable<AccessRule> directRules, Func<Guid, bool> permissionMatches, string scopeType)
        {
            var denied = ScopeContribution.Empty;
            foreach (var rule in directRules)
            {
                if (rule.Effect == AccessEffect.Deny && rule.Origin != AccessRuleOrigin.Delegated && permissionMatches(rule.PermissionId))
                    denied = denied.Union(ScopeContribution.FromRule(rule, scopeType));
            }

            return denied;
        }

        public Task<ScopeContribution> EffectiveScopesAsync(Subject subject, Guid permissionId, string scopeType, bool excludeDelegation, CancellationToken cancellationToken)
            => EffectiveScopesAsync(subject, permissionId, scopeType, excludeDelegation, [], cancellationToken);

        private async Task<ScopeContribution> EffectiveScopesAsync(Subject subject, Guid permissionId, string scopeType, bool excludeDelegation, HashSet<Guid> inProgress, CancellationToken cancellationToken)
        {
            var key = (subject.PrincipalId, subject.ManagingRoleId, permissionId, excludeDelegation, scopeType);
            if (_scopeOutcomes.TryGetValue(key, out var cached))
                return cached;

            if (!inProgress.Add(permissionId))
                return ScopeContribution.Empty;

            try
            {
                var contribution = await EffectiveScopesUncachedAsync(subject, permissionId, scopeType, excludeDelegation, inProgress, cancellationToken);
                _scopeOutcomes[key] = contribution;
                return contribution;
            }
            finally
            {
                inProgress.Remove(permissionId);
            }
        }

        private async Task<ScopeContribution> EffectiveScopesUncachedAsync(Subject subject, Guid permissionId, string scopeType, bool excludeDelegation, HashSet<Guid> inProgress, CancellationToken cancellationToken)
        {
            var prerequisites = TransitivePrerequisites(_directPrerequisites, permissionId);
            var directRules = await GetSubjectRulesAsync(subject.PrincipalId, cancellationToken);
            bool MatchesDeniedPermission(Guid id) => id == permissionId || prerequisites.Contains(id);

            var directDeny = DirectDenyContribution(directRules, MatchesDeniedPermission, scopeType);
            if (directDeny.IsAll)
                return ScopeContribution.Empty;

            var candidates = await CollectCandidatesAsync(subject, directRules, permissionId, excludeDelegation, matchRequestScope: false, validateDelegations: false, cancellationToken);
            var union = ScopeContribution.Empty;
            foreach (var candidate in candidates)
            {
                var granted = CandidateScope(candidate, scopeType, MatchesDeniedPermission);
                if (candidate.Source == CandidateSource.Delegation)
                {
                    if (candidate.Rule.DelegatedFromUserId is not Guid delegatorUserId)
                        continue;

                    var delegator = await LoadSubjectAsync(delegatorUserId, cancellationToken);
                    if (delegator is null)
                        continue;

                    // Fresh in-progress set, same as IsDelegationSourceValidAsync, and never cached (excludeDelegation: true).
                    var source = await EffectiveScopesAsync(delegator, permissionId, scopeType, excludeDelegation: true, [], cancellationToken);
                    granted = granted.Intersect(source);
                }

                union = union.Union(granted);
            }

            foreach (var prerequisiteId in _directPrerequisites[permissionId])
            {
                var required = await EffectiveScopesAsync(subject, prerequisiteId, scopeType, excludeDelegation, inProgress, cancellationToken);
                union = union.Intersect(required);
            }

            return union.Except(directDeny);
        }

        private async Task<List<RuleCandidate>> CollectCandidatesAsync(
            Subject subject,
            List<AccessRule> directRules,
            Guid permissionId,
            bool excludeDelegation,
            bool matchRequestScope,
            bool validateDelegations,
            CancellationToken cancellationToken)
        {
            var candidates = new List<RuleCandidate>();
            var managing = subject.ManagingRoleId is not null;

            if (!managing)
            {
                foreach (var rule in directRules.Where(r => IsAllowRule(r, permissionId) && r.Origin != AccessRuleOrigin.Delegated))
                {
                    if (matchRequestScope && !ScopeMatches(rule))
                        continue;

                    candidates.Add(new RuleCandidate(rule, CandidateSource.DirectUser, null, null, new HashSet<Guid>()));
                }
            }

            IEnumerable<Guid> branchRoleIds = managing ? [subject.ManagingRoleId!.Value] : subject.RoleIds;
            foreach (var branchRoleId in branchRoleIds.Where(id => _roles.TryGet(id, out var branch) && CanGrant(branch)))
            {
                var ancestors = _roles.Ancestors(branchRoleId);

                foreach (var originRoleId in _roles.SelfAndDescendants(branchRoleId))
                {
                    var grantPath = _roles.PathUpTo(originRoleId, branchRoleId);
                    if (!grantPath.All(id => _roles.TryGet(id, out var node) && CanGrant(node)))
                        continue;

                    foreach (var principalId in _principalsByRole[originRoleId])
                    {
                        foreach (var rule in _roleRulesByPrincipal[principalId].Where(r => IsAllowRule(r, permissionId)))
                        {
                            if (matchRequestScope && !ScopeMatches(rule))
                                continue;

                            var path = new HashSet<Guid>(grantPath);
                            path.UnionWith(ancestors);
                            candidates.Add(new RuleCandidate(rule, CandidateSource.RoleBranch, branchRoleId, originRoleId, path));
                        }
                    }
                }
            }

            if (!excludeDelegation && !managing)
            {
                foreach (var rule in directRules.Where(r => IsAllowRule(r, permissionId) && r.Origin == AccessRuleOrigin.Delegated))
                {
                    if (matchRequestScope && !ScopeMatches(rule))
                        continue;
                    if (validateDelegations && !await IsDelegationSourceValidAsync(rule, permissionId, cancellationToken))
                        continue;

                    candidates.Add(new RuleCandidate(rule, CandidateSource.Delegation, null, null, new HashSet<Guid>()));
                }
            }

            return candidates;
        }

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
