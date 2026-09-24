using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Infrastructure.Authorization;

public class AccessEvaluator : IAccessEvaluator
{
    private readonly IApplicationDbContext _context;

    public AccessEvaluator(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AccessDecision> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        // 0. Check Global Super Admin FIRST (before membership check)
        var globalSuperAdminDecision = await CheckGlobalSuperAdminAsync(request, cancellationToken);
        if (globalSuperAdminDecision != null)
            return globalSuperAdminDecision;

        // 1. Validate UserCompany membership
        var userCompany = await _context.UserCompanies
            .Include(uc => uc.Roles)
            .FirstOrDefaultAsync(uc => uc.UserId == request.UserId && uc.CompanyId == request.CompanyId && uc.Status == UserCompanyStatus.Active, cancellationToken);

        if (userCompany == null)
        {
            return new AccessDecision(false, "DENIED_MEMBERSHIP", []);
        }

        // 2. Validate Application
        var application = await _context.Applications
            .FirstOrDefaultAsync(a => a.Code == request.ApplicationCode && a.IsActive, cancellationToken);

        if (application == null)
        {
            return new AccessDecision(false, "DENIED_APPLICATION", []);
        }

        // 3. Validate Permission
        var permission = await (from p in _context.Permissions
            join r in _context.Resources on p.ResourceId equals r.Id
            where r.ApplicationId == application.Id && r.Code + "." + p.ActionCode == request.PermissionCode
            select p).FirstOrDefaultAsync(cancellationToken);

        if (permission == null)
        {
            return new AccessDecision(false, "DENIED_PERMISSION_NOT_FOUND", []);
        }

        // 4. Check Company Super Admin
        var companySuperAdminDecision = await CheckCompanySuperAdminAsync(request, userCompany, cancellationToken);
        if (companySuperAdminDecision != null)
            return companySuperAdminDecision;

        // 5. Build all applicable AccessRules for this user/permission
        var allRules = await GetAllApplicableRulesAsync(userCompany, permission.Id, cancellationToken);

        // 6. Evaluate with full DENY boundary, Role Up/Down, Prerequisite Gates, Delegation
        var decision = await EvaluateRules(request, permission, allRules, userCompany, cancellationToken);

        return decision;
    }

    public async Task EnsureAllowedAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        var decision = await EvaluateAsync(request, cancellationToken);
        if (!decision.Allowed)
        {
            throw new UnauthorizedAccessException($"Access denied: {decision.ReasonCode}");
        }
    }

    private async Task<AccessDecision?> CheckGlobalSuperAdminAsync(AccessRequest request, CancellationToken cancellationToken)
    {
        var globalSuperAdminRole = await _context.Roles
            .Where(r => r.Kind == RoleKind.GlobalSuperAdmin && r.Status == RoleStatus.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (globalSuperAdminRole == null)
            return null;

        // Check if user has Global Super Admin role in ANY company
        var userHasGlobalRole = await _context.UserCompanies
            .Where(uc => uc.UserId == request.UserId && uc.Status == UserCompanyStatus.Active)
            .SelectMany(uc => uc.Roles)
            .AnyAsync(r => r.RoleId == globalSuperAdminRole.Id, cancellationToken);

        if (userHasGlobalRole)
        {
            return new AccessDecision(true, "ALLOWED_GLOBAL_SUPER_ADMIN", [new AccessSource("GlobalSuperAdmin", globalSuperAdminRole.Id.ToString(), "All", [])]);
        }

        return null;
    }

    private async Task<AccessDecision?> CheckCompanySuperAdminAsync(AccessRequest request, UserCompany userCompany, CancellationToken cancellationToken)
    {
        var companySuperAdminRole = await _context.Roles
            .Where(r => r.Kind == RoleKind.CompanySuperAdmin && r.CompanyId == request.CompanyId && r.Status == RoleStatus.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (companySuperAdminRole != null)
        {
            var hasCompany = userCompany.Roles.Any(r => r.RoleId == companySuperAdminRole.Id);
            if (hasCompany)
            {
                return new AccessDecision(true, "ALLOWED_COMPANY_SUPER_ADMIN", [new AccessSource("CompanySuperAdmin", companySuperAdminRole.Id.ToString(), "All", [])]);
            }
        }

        return null;
    }

    private async Task<List<AccessRule>> GetAllApplicableRulesAsync(UserCompany userCompany, Guid permissionId, CancellationToken cancellationToken)
    {
        var allRules = new List<AccessRule>();

        // Direct User Rules
        var directRules = await GetDirectUserRules(userCompany.PrincipalId, permissionId, cancellationToken);
        allRules.AddRange(directRules);

        // Role-based Rules (with Role Up/Down - get all rules from role hierarchy)
        var roleRules = await GetRoleHierarchyRulesAsync(userCompany, permissionId, cancellationToken);
        allRules.AddRange(roleRules);

        // Delegation Rules
        var delegationRules = await GetDelegationRulesAsync(userCompany.PrincipalId, permissionId, cancellationToken);
        allRules.AddRange(delegationRules);

        return allRules;
    }

    private async Task<List<AccessRule>> GetDirectUserRules(Guid principalId, Guid permissionId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return await _context.AccessRules
            .Where(ar => ar.PrincipalId == principalId 
                && ar.PermissionId == permissionId 
                && ar.Effect == AccessEffect.Allow
                && ar.Origin != AccessRuleOrigin.Delegated  // Exclude delegation rules - handled separately
                && ar.Status == AccessRuleStatus.Active
                && (ar.ValidFrom == null || ar.ValidFrom <= now)
                && (ar.ValidUntil == null || ar.ValidUntil > now))
            .Include(ar => ar.Scopes)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<AccessRule>> GetRoleHierarchyRulesAsync(UserCompany userCompany, Guid permissionId, CancellationToken cancellationToken)
    {
        var allRules = new List<AccessRule>();

        var roleIds = userCompany.Roles.Select(r => r.RoleId).ToList();
        if (!roleIds.Any()) return allRules;

        // Get all ancestor roles (Role Up) and descendant roles (Role Down) for each user role
        var allRelevantRoleIds = new HashSet<Guid>(roleIds);

        // Role Up: get all ancestors
        foreach (var roleId in roleIds)
        {
            var ancestors = await GetAncestorRoleIdsAsync(roleId, cancellationToken);
            foreach (var a in ancestors) allRelevantRoleIds.Add(a);
        }

        // Role Down: get all descendants (for DENY boundary)
        foreach (var roleId in roleIds)
        {
            var descendants = await GetDescendantRoleIdsAsync(roleId, cancellationToken);
            foreach (var d in descendants) allRelevantRoleIds.Add(d);
        }

        // Get AuthPrincipals for all relevant roles
        var principalIds = await _context.AuthPrincipals
            .Where(ap => ap.Type == PrincipalType.Role && allRelevantRoleIds.Contains(ap.ReferenceId) && ap.CompanyId == userCompany.CompanyId)
            .Select(ap => ap.Id)
            .ToListAsync(cancellationToken);

        // Get all AccessRules for these principals
        var rules = await _context.AccessRules
            .Where(ar => principalIds.Contains(ar.PrincipalId) && ar.PermissionId == permissionId && ar.Origin != AccessRuleOrigin.Delegated && ar.Status == AccessRuleStatus.Active
                && (ar.ValidFrom == null || ar.ValidFrom <= DateTime.UtcNow)
                && (ar.ValidUntil == null || ar.ValidUntil > DateTime.UtcNow))
            .Include(ar => ar.Scopes)
            .ToListAsync(cancellationToken);

        return rules;
    }

    private async Task<List<Guid>> GetAncestorRoleIdsAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var ancestors = new List<Guid>();
        var currentId = roleId;

        while (true)
        {
            var parent = await _context.Roles
                .Where(r => r.Id == currentId)
                .Select(r => r.ParentRoleId)
                .FirstOrDefaultAsync(cancellationToken);

            if (parent == null) break;

            ancestors.Add(parent.Value);
            currentId = parent.Value;
        }

        return ancestors;
    }

    private async Task<List<Guid>> GetDescendantRoleIdsAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var descendants = new List<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(roleId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var children = await _context.Roles
                .Where(r => r.ParentRoleId == current)
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);

            foreach (var child in children)
            {
                descendants.Add(child);
                queue.Enqueue(child);
            }
        }

        return descendants;
    }

    private async Task<List<AccessRule>> GetDelegationRulesAsync(Guid principalId, Guid permissionId, CancellationToken cancellationToken)
    {
        // Find delegations TO this principal (delegatee)
        var delegations = await _context.AccessRules
            .Where(ar => ar.PrincipalId == principalId
                && ar.PermissionId == permissionId
                && ar.Status == AccessRuleStatus.Active
                && ar.Origin == AccessRuleOrigin.Delegated
                && (ar.ValidUntil == null || ar.ValidUntil > DateTime.UtcNow)
                && (ar.ValidFrom == null || ar.ValidFrom <= DateTime.UtcNow))
            .Include(ar => ar.Scopes)
            .ToListAsync(cancellationToken);

        // Additional in-memory filter for expiration (InMemory DB safety)
        var validDelegations = delegations
            .Where(d => d.ValidUntil == null || d.ValidUntil > DateTime.UtcNow)
            .Where(d => d.ValidFrom == null || d.ValidFrom <= DateTime.UtcNow)
            .ToList();

        // Verify delegator still has the permission
        var validDelegationsFinal = new List<AccessRule>();
        foreach (var delegation in validDelegations)
        {
            if (delegation.DelegatedFromUserId == null)
                continue;

            var delegatorUserCompany = await _context.UserCompanies
                .Where(uc => uc.UserId == delegation.DelegatedFromUserId && uc.Status == UserCompanyStatus.Active)
                .Include(uc => uc.Roles)
                .FirstOrDefaultAsync();

            if (delegatorUserCompany == null)
                continue;

            // Check if delegator still has the permission (direct check, no recursion)
            var delegatorHasPermission = await CheckUserHasPermissionDirect(delegatorUserCompany, permissionId, cancellationToken);

            if (delegatorHasPermission)
            {
                validDelegationsFinal.Add(delegation);
            }
        }

        return validDelegationsFinal;
    }

    private async Task<bool> CheckUserHasPermissionDirect(UserCompany userCompany, Guid permissionId, CancellationToken cancellationToken)
    {
        // Check direct user rules
        var directRules = await _context.AccessRules
            .Where(ar => ar.PrincipalId == userCompany.PrincipalId && ar.PermissionId == permissionId && ar.Effect == AccessEffect.Allow && ar.Origin != AccessRuleOrigin.Delegated && ar.Status == AccessRuleStatus.Active
                && (ar.ValidFrom == null || ar.ValidFrom <= DateTime.UtcNow)
                && (ar.ValidUntil == null || ar.ValidUntil > DateTime.UtcNow))
            .ToListAsync(cancellationToken);
        if (directRules.Any()) return true;

        // Check role rules (simplified - no delegation check to avoid recursion)
        var roleIds = userCompany.Roles.Select(r => r.RoleId).ToList();
        if (!roleIds.Any()) return false;

        var allRelevantRoleIds = new HashSet<Guid>(roleIds);
        foreach (var roleId in roleIds)
        {
            var ancestors = await GetAncestorRoleIdsAsync(roleId, cancellationToken);
            foreach (var a in ancestors) allRelevantRoleIds.Add(a);
        }

        foreach (var roleId in roleIds)
        {
            var descendants = await GetDescendantRoleIdsAsync(roleId, cancellationToken);
            foreach (var d in descendants) allRelevantRoleIds.Add(d);
        }

        var principalIds = await _context.AuthPrincipals
            .Where(ap => ap.Type == PrincipalType.Role && allRelevantRoleIds.Contains(ap.ReferenceId) && ap.CompanyId == userCompany.CompanyId)
            .Select(ap => ap.Id)
            .ToListAsync();

        var roleRules = await _context.AccessRules
            .Where(ar => principalIds.Contains(ar.PrincipalId) && ar.PermissionId == permissionId && ar.Effect == AccessEffect.Allow && ar.Status == AccessRuleStatus.Active
                && (ar.ValidFrom == null || ar.ValidFrom <= DateTime.UtcNow)
                && (ar.ValidUntil == null || ar.ValidUntil > DateTime.UtcNow))
            .ToListAsync(cancellationToken);
        
        Console.WriteLine($"[DEBUG] Role rules found: {roleRules.Count}");
        foreach (var r in roleRules)
        {
            Console.WriteLine($"[DEBUG]   Rule: Id={r.Id}, Effect={r.Effect}, Origin={r.Origin}, PrincipalId={r.PrincipalId}, PermissionId={r.PermissionId}");
        }

        return roleRules.Any();
    }

    private async Task<AccessDecision> EvaluateRules(AccessRequest request, Permission permission, List<AccessRule> allRules, UserCompany userCompany, CancellationToken cancellationToken)
    {
        var sources = new List<AccessSource>();

        // Separate ALLOW and DENY rules
        var allowRules = allRules.Where(r => r.Effect == AccessEffect.Allow).ToList();
        var denyRules = allRules.Where(r => r.Effect == AccessEffect.Deny).ToList();

        // BRANCH-LOCAL DENY: For each user direct role (branch), check if there's a DENY in that branch
        // Build a map of branch root (user's direct role) -> has DENY
        var userRoleIds = userCompany.Roles.Select(r => r.RoleId).ToList();
        var blockedBranches = new HashSet<Guid>();

        foreach (var userRoleId in userRoleIds)
        {
            // Get all roles in this branch (ancestors + descendants + self)
            var branchRoleIds = new HashSet<Guid>();
            
            // Add self
            branchRoleIds.Add(userRoleId);
            
            // Ancestors
            var ancestors = await GetAncestorRoleIdsAsync(userRoleId, cancellationToken);
            foreach (var a in ancestors) branchRoleIds.Add(a);
            
            // Descendants
            var descendants = await GetDescendantRoleIdsAsync(userRoleId, cancellationToken);
            foreach (var d in descendants) branchRoleIds.Add(d);

            // Check if this branch has a DENY for the requested permission/scope
            var branchDenyRules = denyRules
                .Where(r => r.BranchRootId.HasValue && IsScopeMatch(r, request.ScopeType, request.ScopeKey))
                .Where(r => branchRoleIds.Contains(r.BranchRootId ?? Guid.Empty))
                .ToList();

            if (branchDenyRules.Any())
            {
                blockedBranches.Add(userRoleId);
            }
        }

        // Filter DENY rules for scope match
        var scopeMatchedDenyRules = denyRules.Where(r => IsScopeMatch(r, request.ScopeType, request.ScopeKey)).ToList();

        // If any DENY exists (regardless of branch), return DENIED_EXPLICIT_DENY
        // This handles self-DENY and cross-branch DENY that applies
        if (scopeMatchedDenyRules.Any())
        {
            return new AccessDecision(false, "DENIED_EXPLICIT_DENY", [new AccessSource("Deny", "explicit", "All", [])]);
        }

        // Check Prerequisite Gates (Edit → Read, etc.)
        var prereqResult = await CheckPrerequisiteGates(request, permission, allRules.Where(r => r.Effect == AccessEffect.Allow).ToList(), allRules.Where(r => r.Effect == AccessEffect.Deny).ToList(), userCompany);
        if (prereqResult != null)
            return prereqResult;

        // Evaluate ALLOW rules with Scope
        var allowedRules = allowRules
            .Where(r => IsScopeMatch(r, request.ScopeType, request.ScopeKey))
            .ToList();

        if (allowedRules.Any())
        {
            var sourceTypes = allowedRules.Select(r => r.Origin.ToString()).Distinct().ToList();
            sources.Add(new AccessSource(string.Join(",", sourceTypes), "multiple", "All", []));
            return new AccessDecision(true, "ALLOWED", sources);
        }

        // Check if DENY exists for this permission (even without scope match)
        if (denyRules.Any())
        {
            return new AccessDecision(false, "DENIED_EXPLICIT_DENY", sources);
        }

        return new AccessDecision(false, "DENIED_NO_PERMISSION", sources);
    }

    private async Task<AccessDecision?> CheckPrerequisiteGates(AccessRequest request, Permission permission, List<AccessRule> allowRules, List<AccessRule> denyRules, UserCompany userCompany)
    {
        // Get all prerequisite permissions (transitive)
        var prerequisites = GetPrerequisitePermissions(permission.Id);
        if (!prerequisites.Any()) return null;

        foreach (var prereqId in prerequisites)
        {
            var prereqPermission = await _context.Permissions.FirstOrDefaultAsync(p => p.Id == prereqId);
            if (prereqPermission == null) continue;

            // Get all applicable rules for the prerequisite permission
            var prereqAllRules = await GetAllApplicableRulesAsync(userCompany, prereqId, CancellationToken.None);
            var prereqAllows = prereqAllRules.Where(r => r.Effect == AccessEffect.Allow && IsScopeMatch(r, request.ScopeType, request.ScopeKey)).ToList();
            var prereqDenies = prereqAllRules.Where(r => r.Effect == AccessEffect.Deny && IsScopeMatch(r, request.ScopeType, request.ScopeKey)).ToList();
            
            if (prereqDenies.Any() || !prereqAllows.Any())
            {
                return new AccessDecision(false, "DENIED_PREREQUISITE_GATE", [new AccessSource("PrerequisiteMissing", prereqPermission.Resource?.Code + "." + prereqPermission.ActionCode, "All", [])]);
            }
        }

        return null;
    }

    private HashSet<Guid> GetPrerequisitePermissions(Guid permissionId)
    {
        var prerequisites = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(permissionId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var implications = _context.PermissionImplications
                .Where(i => i.PermissionId == current)
                .Select(i => i.RequiredPermissionId)
                .ToList();

            foreach (var prereq in implications)
            {
                if (prerequisites.Add(prereq))
                    queue.Enqueue(prereq);
            }
        }

        return prerequisites;
    }

    private static bool IsScopeMatch(AccessRule rule, string? scopeType, string? scopeKey)
    {
        if (rule.ScopeMode == ScopeMode.None)
            return scopeType == null;

        if (rule.ScopeMode == ScopeMode.All)
            return true;

        if (rule.ScopeMode == ScopeMode.Selected)
        {
            if (scopeType == null || scopeKey == null)
                return false;

            return rule.Scopes.Any(s => s.ScopeType == scopeType && s.ScopeKey == scopeKey);
        }

        return false;
    }
}