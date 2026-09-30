using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.AccessControl.Roles.Commands;

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
    private readonly ICurrentWorkspace _workspace;

    public UpdateRolePermissionsCommandHandler(IApplicationDbContext context, ICurrentWorkspace workspace)
    {
        _context = context;
        _workspace = workspace;
    }

    public async Task Handle(UpdateRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var role = await _context.Roles
            .InAccessControlApplication(_context)
            .FirstOrDefaultAsync(r => r.Id == request.RoleId && r.CompanyId == companyId, cancellationToken);

        Guard.Against.NotFound(request.RoleId, role);

        if (role.Kind != RoleKind.Standard)
            throw new ForbiddenAccessException();

        var principal = await _context.AuthPrincipals
            .Include(ap => ap.AccessRules)
                .ThenInclude(ar => ar.Scopes)
            .FirstOrDefaultAsync(ap => ap.Type == PrincipalType.Role && ap.ReferenceId == role.Id && ap.CompanyId == role.CompanyId && ap.ApplicationId == role.ApplicationId, cancellationToken);

        Guard.Against.NotFound(role.Id, principal);

        foreach (var entry in request.Entries)
        {
            var resourceExists = await _context.Resources
                .AnyAsync(r => r.Id == entry.ResourceId && r.ApplicationId == role.ApplicationId, cancellationToken);

            if (!resourceExists)
                throw new NotFoundException(entry.ResourceId.ToString(), nameof(Resource));

            foreach (var action in entry.Actions)
            {
                var permission = await _context.Permissions
                    .FirstOrDefaultAsync(p => p.ResourceId == entry.ResourceId && p.ActionCode == action.ActionCode, cancellationToken);

                if (permission == null)
                    throw new NotFoundException($"{entry.ResourceId}/{action.ActionCode}", nameof(Permission));

                var rule = principal.AccessRules.FirstOrDefault(ar => ar.PermissionId == permission.Id && ar.Effect == action.Effect);

                if (rule == null)
                {
                    rule = new AccessRule(principal.Id, permission.Id, action.Effect);
                    principal.AddAccessRule(rule);
                }

                var selected = action.ScopeKeys is { Length: > 0 } && !string.IsNullOrEmpty(action.ScopeType);
                rule.SetScopeMode(selected ? ScopeMode.Selected : ScopeMode.None);
                rule.ClearScopes();

                if (selected)
                {
                    foreach (var scopeKey in action.ScopeKeys!.Distinct())
                    {
                        rule.AddScope(action.ScopeType!, scopeKey);
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
