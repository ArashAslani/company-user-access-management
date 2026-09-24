using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.AccessControl;
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
        var targetRole = await _context.Roles
            .Include(r => r.AccessRules)
            .FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);

        if (targetRole == null)
            throw new InvalidOperationException("Target role not found.");

        var sourceRole = await _context.Roles
            .Include(r => r.AccessRules)
                .ThenInclude(ar => ar.Scopes)
            .FirstOrDefaultAsync(r => r.Id == request.SourceRoleId, cancellationToken);

        if (sourceRole == null)
            throw new InvalidOperationException("Source role not found.");

        // Get target role's AuthPrincipal
        var targetPrincipal = await _context.AuthPrincipals
            .FirstOrDefaultAsync(ap => ap.Type == PrincipalType.Role && ap.ReferenceId == targetRole.Id && ap.CompanyId == targetRole.CompanyId && ap.ApplicationId == targetRole.ApplicationId);

        if (targetPrincipal == null)
        {
            targetPrincipal = new AuthPrincipal(PrincipalType.Role, targetRole.Id, targetRole.CompanyId, targetRole.ApplicationId);
            _context.AuthPrincipals.Add(targetPrincipal);
        }

        if (request.Mode == "REPLACE")
        {
            // Remove existing access rules
            var existingRules = targetPrincipal.AccessRules.ToList();
            foreach (var rule in existingRules)
            {
                targetPrincipal.RemoveAccessRule(rule.Id);
            }
        }

        int copied = 0;
        foreach (var sourceRule in sourceRole.AccessRules)
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
        var app = await _context.Applications.FirstOrDefaultAsync(a => a.Id == targetRole.ApplicationId);
        if (app != null)
            app.IncrementPolicyRevision();

        await _context.SaveChangesAsync(cancellationToken);

        return copied;
    }
}
