using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.AccessControl;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.AccessControl.Roles.Commands;

public record UpdateRolePermissionsCommand : IRequest
{
    public Guid RoleId { get; init; }
    public RolePermissionEntry[] Entries { get; init; } = [];
}

public record RolePermissionEntry
{
    public Guid ResourceId { get; init; }
    public RolePermissionAction[] Actions { get; init; } = [];
}

public record RolePermissionAction
{
    public string ActionCode { get; init; } = null!;
    public AccessEffect Effect { get; init; }
    public string? ScopeType { get; init; }
    public string[]? ScopeKeys { get; init; }
}

public class UpdateRolePermissionsCommandHandler : IRequestHandler<UpdateRolePermissionsCommand>
{
    private readonly IApplicationDbContext _context;

    public UpdateRolePermissionsCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(UpdateRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        var role = await _context.Roles
            .Include(r => r.AccessRules)
                .ThenInclude(ar => ar.Scopes)
            .FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);

        if (role == null)
            throw new InvalidOperationException("Role not found.");

        // Get role's AuthPrincipal
        var principal = await _context.AuthPrincipals
            .FirstOrDefaultAsync(ap => ap.Type == PrincipalType.Role && ap.ReferenceId == role.Id && ap.CompanyId == role.CompanyId && ap.ApplicationId == role.ApplicationId, cancellationToken);

        if (principal == null)
        {
            principal = new AuthPrincipal(PrincipalType.Role, role.Id, role.CompanyId, role.ApplicationId);
            _context.AuthPrincipals.Add(principal);
        }

        foreach (var entry in request.Entries)
        {
            var resource = await _context.Resources
                .FirstOrDefaultAsync(r => r.Id == entry.ResourceId, cancellationToken);

            if (resource == null)
                continue;

            foreach (var action in entry.Actions)
            {
                var permission = await _context.Permissions
                    .FirstOrDefaultAsync(p => p.ResourceId == entry.ResourceId && p.ActionCode == action.ActionCode, cancellationToken);

                if (permission == null)
                    continue;

                // Find or create AccessRule
                var rule = principal.AccessRules.FirstOrDefault(ar => ar.PermissionId == permission.Id && ar.Effect == action.Effect);

                if (rule == null)
                {
                    rule = new AccessRule(principal.Id, permission.Id, action.Effect);
                    principal.AddAccessRule(rule);
                }

                rule.SetScopeMode(action.ScopeKeys != null && action.ScopeKeys.Length > 0 ? ScopeMode.Selected : ScopeMode.None);

                if (action.ScopeKeys != null && action.ScopeKeys.Length > 0 && !string.IsNullOrEmpty(action.ScopeType))
                {
                    rule.ClearScopes();
                    foreach (var scopeKey in action.ScopeKeys)
                    {
                        rule.AddScope(action.ScopeType, scopeKey);
                    }
                }
            }
        }

        // Update policy revision
        var app = await _context.Applications.FirstOrDefaultAsync(a => a.Id == role.ApplicationId, cancellationToken);
        if (app != null)
            app.IncrementPolicyRevision();

        await _context.SaveChangesAsync(cancellationToken);
    }
}