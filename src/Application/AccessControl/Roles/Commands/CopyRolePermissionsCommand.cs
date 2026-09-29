using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.AccessControl.Roles.Commands;

public record CopyRolePermissionsCommand : IRequest<int>
{
    public Guid RoleId { get; init; }
    public Guid SourceRoleId { get; init; }
    public string Mode { get; init; } = "APPEND"; // APPEND | REPLACE
}

public class CopyRolePermissionsCommandHandler : IRequestHandler<CopyRolePermissionsCommand, int>
{
    private readonly IApplicationDbContext _context;

    public CopyRolePermissionsCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<int> Handle(CopyRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        if (request.RoleId == request.SourceRoleId)
            throw new DomainRuleViolationException("ROLE_COPY_SAME_ROLE", "A role cannot copy permissions from itself.");

        if (request.Mode is not ("APPEND" or "REPLACE"))
            throw new DomainRuleViolationException("ROLE_COPY_INVALID_MODE", "Mode must be APPEND or REPLACE.");

        var targetRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);

        Guard.Against.NotFound(request.RoleId, targetRole);

        var sourceRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.Id == request.SourceRoleId && r.CompanyId == targetRole.CompanyId && r.ApplicationId == targetRole.ApplicationId, cancellationToken);

        Guard.Against.NotFound(request.SourceRoleId, sourceRole);

        var targetPrincipal = await LoadRolePrincipalAsync(targetRole, cancellationToken);
        var sourcePrincipal = await LoadRolePrincipalAsync(sourceRole, cancellationToken);

        if (request.Mode == "REPLACE")
        {
            var existingRules = targetPrincipal.AccessRules.ToList();
            foreach (var rule in existingRules)
            {
                targetPrincipal.RemoveAccessRule(rule.Id);
            }
        }

        int copied = 0;
        foreach (var sourceRule in sourcePrincipal.AccessRules.Where(r => r.Origin != AccessRuleOrigin.Delegated))
        {
            if (request.Mode == "APPEND" && targetPrincipal.AccessRules.Any(ar => ar.PermissionId == sourceRule.PermissionId && ar.Effect == sourceRule.Effect))
                continue;

            var newRule = new AccessRule(targetPrincipal.Id, sourceRule.PermissionId, sourceRule.Effect, sourceRule.Origin, sourceRule.ScopeMode, sourceRule.ValidFrom, sourceRule.ValidUntil);
            
            foreach (var scope in sourceRule.Scopes)
            {
                newRule.AddScope(scope.ScopeType, scope.ScopeKey);
            }

            targetPrincipal.AddAccessRule(newRule);
            copied++;
        }

        // Update policy revision
        var app = await _context.Applications.FirstOrDefaultAsync(a => a.Id == targetRole.ApplicationId, cancellationToken);
        if (app != null)
            app.IncrementPolicyRevision();

        await _context.SaveChangesAsync(cancellationToken);

        return copied;
    }

    private async Task<AuthPrincipal> LoadRolePrincipalAsync(Role role, CancellationToken cancellationToken)
    {
        var principal = await _context.AuthPrincipals
            .Include(ap => ap.AccessRules)
                .ThenInclude(ar => ar.Scopes)
            .FirstOrDefaultAsync(ap => ap.Type == PrincipalType.Role && ap.ReferenceId == role.Id && ap.CompanyId == role.CompanyId && ap.ApplicationId == role.ApplicationId, cancellationToken);

        Guard.Against.NotFound(role.Id, principal);
        return principal;
    }
}
