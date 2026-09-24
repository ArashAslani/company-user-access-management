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

        // 4. Check Super Admin
        var superAdminDecision = await CheckSuperAdminAsync(request, userCompany, cancellationToken);
        if (superAdminDecision != null)
            return superAdminDecision;

        // 5. Build all applicable AccessRules for this user/permission
        var allRules = await GetAllApplicableRulesAsync(userCompany, permission.Id, cancellationToken);

        // 6. Evaluate with full DENY boundary, Role Up/Down, Prerequisite Gates, Delegation
        var decision = await EvaluateRules(request, permission, allRules, userCompany);

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

    private async Task<AccessDecision?> CheckSuperAdminAsync(AccessRequest request, UserCompany userCompany, CancellationToken cancellationToken)
    {
        // Check Global Super Admin (only in root company)
        var globalSuperAdminRole = await _context.Roles
            .Where(r => r.Kind == RoleKind.GlobalSuperAdmin && r.Status == RoleStatus.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (globalSuperAdminRole != null)
        {
            var globalPrincipalIds = await _context.AuthPrincipals
                .Where(ap => ap.Type == PrincipalType.Role && ap.ReferenceId == globalSuperAdminRole.Id && ap.CompanyId == userCompany.CompanyId)
                .Select(ap => ap.Id)
                .ToListAsync(cancellationToken);

            var hasGlobal = globalPrincipalIds.Any(pid => userCompany.Roles.Any(r => r.RoleId == globalSuperAdminRole.Id));
            if (hasGlobal)
            {
                return new AccessDecision(true, "ALLOWED_GLOBAL_SUPER_ADMIN", [new AccessSource("GlobalSuperAdmin", globalSuperAdminRole.Id.ToString(), "All", [])]);
            }
        }

        // Check Company Super Admin
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
        return await _context.AccessRules
            .Where(ar => ar.PrincipalId == principalId && ar.PermissionId == permissionId && ar.Status == AccessRuleStatus.Active)
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
            .Where(ar => principalIds.Contains(ar.PrincipalId) && ar.PermissionId == permissionId && ar.Status == AccessRuleStatus.Active)
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
        return await _context.AccessRules
            .Where(ar => ar.DelegatedFromUserId != null && ar.DelegatedFromUserId == _context.UserCompanies
                    .Where(uc => uc.PrincipalId == principalId)
                    .Select(uc => uc.UserId)
                    .FirstOrDefault()
                && ar.PermissionId == permissionId
                && ar.Status == AccessRuleStatus.Active
                && ar.Origin == AccessRuleOrigin.Delegated
                && (ar.ValidUntil == null || ar.ValidUntil > DateTime.UtcNow))
            .Include(ar => ar.Scopes)
            .ToListAsync(cancellationToken);
    }

    private async Task<AccessDecision> EvaluateRules(AccessRequest request, Permission permission, List<AccessRule> allRules, UserCompany userCompany)
    {
        var sources = new List<AccessSource>();

        // Separate ALLOW and DENY rules
        var allowRules = allRules.Where(r => r.Effect == AccessEffect.Allow).ToList();
        var denyRules = allRules.Where(r => r.Effect == AccessEffect.Deny).ToList();

        // Check DENY first (DENY boundary)
        var denyResult = CheckDenyBoundary(request, permission, denyRules, userCompany);
        if (denyResult != null)
            return denyResult;

        // Check Prerequisite Gates (Edit → Read, etc.)
        var prereqResult = await CheckPrerequisiteGates(request, permission, allowRules, denyRules, userCompany);
        if (prereqResult != null)
            return prereqResult;

        // Evaluate ALLOW rules with Scope
        var allowedRules = allowRules.Where(r => IsScopeMatch(r, request.ScopeType, request.ScopeKey)).ToList();
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

    private AccessDecision? CheckDenyBoundary(AccessRequest request, Permission permission, List<AccessRule> denyRules, UserCompany userCompany)
    {
        // DENY on this permission in same branch/scope
        var relevantDenies = denyRules.Where(r => IsScopeMatch(r, request.ScopeType, request.ScopeKey)).ToList();

        if (relevantDenies.Any())
        {
            return new AccessDecision(false, "DENIED_EXPLICIT_DENY", [new AccessSource("Deny", "explicit", "All", [])]);
        }

        // Check DENY on ancestor roles (Role Down boundary)
        // If any ancestor role in the user's role hierarchy has DENY on this permission
        // This is handled by GetRoleHierarchyRulesAsync including descendants

        return null;
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

    private async Task<List<AccessRule>> GetAllDenyRulesForPermissionAsync(UserCompany userCompany, Guid permissionId, string? scopeType, string? scopeKey)
    {
        var denyRules = new List<AccessRule>();

        // Direct User DENY
        var directDenies = await _context.AccessRules
            .Where(ar => ar.PrincipalId == userCompany.PrincipalId && ar.PermissionId == permissionId && ar.Effect == AccessEffect.Deny && ar.Status == AccessRuleStatus.Active)
            .Include(ar => ar.Scopes)
            .ToListAsync();
        denyRules.AddRange(directDenies);

        // Role DENY (including hierarchy)
        var roleIds = userCompany.Roles.Select(r => r.RoleId).ToList();
        if (roleIds.Any())
        {
            var allRelevantRoleIds = new HashSet<Guid>(roleIds);
            foreach (var roleId in roleIds)
            {
                var ancestors = await GetAncestorRoleIdsAsync(roleId, default);
                foreach (var a in ancestors) allRelevantRoleIds.Add(a);
            }

            var principalIds = await _context.AuthPrincipals
                .Where(ap => ap.Type == PrincipalType.Role && allRelevantRoleIds.Contains(ap.ReferenceId) && ap.CompanyId == userCompany.CompanyId)
                .Select(ap => ap.Id)
                .ToListAsync();

            var roleDenies = await _context.AccessRules
                .Where(ar => principalIds.Contains(ar.PrincipalId) && ar.PermissionId == permissionId && ar.Effect == AccessEffect.Deny && ar.Status == AccessRuleStatus.Active)
                .Include(ar => ar.Scopes)
                .ToListAsync();
            denyRules.AddRange(roleDenies);
        }

        return denyRules.Where(d => IsScopeMatch(d, scopeType, scopeKey)).ToList();
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
