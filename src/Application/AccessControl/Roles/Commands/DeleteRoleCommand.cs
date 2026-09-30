using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Common;

namespace CompanyAccessManagement.Application.AccessControl.Roles.Commands;

public record DeleteRoleCommand : IRequest
{
    public Guid Id { get; init; }
}

public class DeleteRoleCommandHandler : IRequestHandler<DeleteRoleCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;

    public DeleteRoleCommandHandler(IApplicationDbContext context, ICurrentWorkspace workspace)
    {
        _context = context;
        _workspace = workspace;
    }

    public async Task Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var role = await _context.Roles
            .Include(r => r.UserRoles)
            .FirstOrDefaultAsync(r => r.Id == request.Id && r.CompanyId == companyId, cancellationToken);

        Guard.Against.NotFound(request.Id, role);

        if (role.Kind != RoleKind.Standard)
            throw new ForbiddenAccessException();

        var hasRules = await (from ap in _context.AuthPrincipals
                              join ar in _context.AccessRules on ap.Id equals ar.PrincipalId
                              where ap.Type == PrincipalType.Role && ap.ReferenceId == role.Id
                                  && ap.CompanyId == role.CompanyId && ap.ApplicationId == role.ApplicationId
                              select ar.Id)
            .AnyAsync(cancellationToken);

        if (role.UserRoles.Count > 0 || hasRules)
            throw new DomainRuleViolationException("ROLE_IN_USE", "Cannot delete a role with active assignments or permissions.");

        // Soft delete - deactivate
        role.SetStatus(RoleStatus.Inactive);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
