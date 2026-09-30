using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;

namespace CompanyAccessManagement.Application.AccessControl.Roles.Commands.BulkAssignRole;

public record BulkAssignRoleCommand(
    Guid RoleId,
    Guid[] UserCompanyIds,
    DateTime? ValidFrom = null,
    DateTime? ValidUntil = null) : IRequest<BulkAssignRoleResult>;

public record BulkAssignRoleResult(int CreatedUserRoleCount);

public class BulkAssignRoleCommandHandler : IRequestHandler<BulkAssignRoleCommand, BulkAssignRoleResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;

    public BulkAssignRoleCommandHandler(IApplicationDbContext context, ICurrentWorkspace workspace)
    {
        _context = context;
        _workspace = workspace;
    }

    public async Task<BulkAssignRoleResult> Handle(BulkAssignRoleCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var role = await _context.Roles
            .FirstOrDefaultAsync(r => r.Id == request.RoleId && r.CompanyId == companyId, cancellationToken);

        Guard.Against.NotFound(request.RoleId, role);

        if (role.Kind != RoleKind.Standard)
            throw new ForbiddenAccessException();

        var requestedIds = request.UserCompanyIds.Distinct().ToArray();

        var userCompanies = await _context.UserCompanies
            .Include(uc => uc.Roles)
            .Where(uc => requestedIds.Contains(uc.Id) && uc.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        var missing = requestedIds.Except(userCompanies.Select(uc => uc.Id)).FirstOrDefault();
        if (missing != Guid.Empty)
            throw new NotFoundException(missing.ToString(), "UserCompany");

        int created = 0;
        foreach (var uc in userCompanies)
        {
            if (uc.Roles.Any(r => r.RoleId == role.Id))
                continue;

            uc.AddRole(role.Id);
            created++;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new BulkAssignRoleResult(created);
    }
}
