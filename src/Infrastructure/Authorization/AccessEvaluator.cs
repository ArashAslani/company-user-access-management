using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.Authorization;

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
        var permission = await _context.Permissions
            .Include(p => p.Resource)
            .FirstOrDefaultAsync(p => p.Resource != null && p.Resource.ApplicationId == application.Id && p.Resource.Code + "." + p.ActionCode == request.PermissionCode, cancellationToken);

        if (permission == null)
        {
            return new AccessDecision(false, "DENIED_PERMISSION_NOT_FOUND", []);
        }

        var sources = new List<AccessSource>();

        // Check Direct User Permission
        var directRules = await GetDirectUserRules(userCompany.PrincipalId, permission.Id, cancellationToken);
        if (directRules.Any(r => r.Effect == AccessEffect.Allow && IsScopeMatch(r, request.ScopeType, request.ScopeKey)))
        {
            sources.Add(new AccessSource("DirectUser", userCompany.PrincipalId.ToString(), "All", []));
            return new AccessDecision(true, "ALLOWED_DIRECT", sources);
        }

        // Check Role-based permissions
        var roleRules = await GetRoleRules(userCompany, permission.Id, cancellationToken);
        var allowedByRole = roleRules.Any(r => r.Effect == AccessEffect.Allow && IsScopeMatch(r, request.ScopeType, request.ScopeKey));
        if (allowedByRole)
        {
            sources.Add(new AccessSource("Role", userCompany.PrincipalId.ToString(), "All", []));
            return new AccessDecision(true, "ALLOWED_ROLE", sources);
        }

        // Check RoleGroup permissions
        var roleGroupRules = await GetRoleGroupRules(userCompany, permission.Id, cancellationToken);
        var allowedByRoleGroup = roleGroupRules.Any(r => r.Effect == AccessEffect.Allow && IsScopeMatch(r, request.ScopeType, request.ScopeKey));
        if (allowedByRoleGroup)
        {
            sources.Add(new AccessSource("RoleGroup", userCompany.PrincipalId.ToString(), "All", []));
            return new AccessDecision(true, "ALLOWED_ROLE_GROUP", sources);
        }

        // Check Deny rules
        var hasDeny = directRules.Any(r => r.Effect == AccessEffect.Deny) ||
                      roleRules.Any(r => r.Effect == AccessEffect.Deny) ||
                      roleGroupRules.Any(r => r.Effect == AccessEffect.Deny);

        if (hasDeny)
        {
            return new AccessDecision(false, "DENIED_EXPLICIT_DENY", sources);
        }

        return new AccessDecision(false, "DENIED_NO_PERMISSION", sources);
    }

    public async Task EnsureAllowedAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        var decision = await EvaluateAsync(request, cancellationToken);
        if (!decision.Allowed)
        {
            throw new UnauthorizedAccessException($"Access denied: {decision.ReasonCode}");
        }
    }

    private async Task<List<AccessRule>> GetDirectUserRules(Guid principalId, Guid permissionId, CancellationToken cancellationToken)
    {
        return await _context.AccessRules
            .Where(ar => ar.PrincipalId == principalId && ar.PermissionId == permissionId && ar.Status == AccessRuleStatus.Active)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<AccessRule>> GetRoleRules(UserCompany userCompany, Guid permissionId, CancellationToken cancellationToken)
    {
        var roleIds = userCompany.Roles.Select(r => r.RoleId).ToList();
        if (!roleIds.Any()) return [];

        var principalIds = await _context.AuthPrincipals
            .Where(ap => ap.Type == PrincipalType.Role && roleIds.Contains(ap.ReferenceId) && ap.CompanyId == userCompany.CompanyId)
            .Select(ap => ap.Id)
            .ToListAsync(cancellationToken);

        return await _context.AccessRules
            .Where(ar => principalIds.Contains(ar.PrincipalId) && ar.PermissionId == permissionId && ar.Status == AccessRuleStatus.Active)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<AccessRule>> GetRoleGroupRules(UserCompany userCompany, Guid permissionId, CancellationToken cancellationToken)
    {
        var roleIds = userCompany.Roles.Select(r => r.RoleId).ToList();
        if (!roleIds.Any()) return [];

        var roleGroupIds = await _context.RoleGroupRoles
            .Where(rgr => roleIds.Contains(rgr.RoleId))
            .Select(rgr => rgr.RoleGroupId)
            .ToListAsync(cancellationToken);

        if (!roleGroupIds.Any()) return [];

        var principalIds = await _context.AuthPrincipals
            .Where(ap => ap.Type == PrincipalType.RoleGroup && roleGroupIds.Contains(ap.ReferenceId) && ap.CompanyId == userCompany.CompanyId)
            .Select(ap => ap.Id)
            .ToListAsync(cancellationToken);

        return await _context.AccessRules
            .Where(ar => principalIds.Contains(ar.PrincipalId) && ar.PermissionId == permissionId && ar.Status == AccessRuleStatus.Active)
            .ToListAsync(cancellationToken);
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