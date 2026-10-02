using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;

namespace CompanyAccessManagement.Application.AccessControl.Roles.Commands;

public sealed record RemoveUserRoleCommand(Guid RoleId, Guid UserCompanyId) : IRequest;

public sealed class RemoveUserRoleCommandHandler : IRequestHandler<RemoveUserRoleCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;
    private readonly IAdminAuthority _adminAuthority;
    private readonly IAuditWriter _audit;

    public RemoveUserRoleCommandHandler(IApplicationDbContext context, ICurrentWorkspace workspace, IAdminAuthority adminAuthority, IAuditWriter audit)
    {
        _context = context;
        _workspace = workspace;
        _adminAuthority = adminAuthority;
        _audit = audit;
    }

    public async Task Handle(RemoveUserRoleCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var role = await _context.Roles
            .InAccessControlApplication(_context)
            .FirstOrDefaultAsync(r => r.Id == request.RoleId && r.CompanyId == companyId, cancellationToken);
        Guard.Against.NotFound(request.RoleId, role);

        if (role.Kind != RoleKind.Standard)
            throw new ForbiddenAccessException();

        await _adminAuthority.EnsureCanAssignAsync(role.Id, cancellationToken);

        var membership = await _context.UserCompanies
            .Include(uc => uc.Roles)
            .FirstOrDefaultAsync(uc => uc.Id == request.UserCompanyId && uc.CompanyId == companyId, cancellationToken);
        Guard.Against.NotFound(request.UserCompanyId, membership);

        var userRole = membership.Roles.FirstOrDefault(r => r.RoleId == role.Id)
            ?? throw new NotFoundException(request.RoleId.ToString(), nameof(UserRole));

        membership.RemoveRole(role.Id);
        _audit.Write(new AuditWriteRequest(
            AuditEventTypes.RoleRemoved,
            nameof(UserRole),
            userRole.Id,
            AccessRuleSourceType.Role,
            BeforeData: AuditJson.Serialize(new { userCompanyId = membership.Id, roleId = role.Id }),
            TargetPrincipalId: membership.PrincipalId,
            ApplicationId: role.ApplicationId,
            CompanyId: companyId));

        await _context.SaveChangesAsync(cancellationToken);
    }
}
