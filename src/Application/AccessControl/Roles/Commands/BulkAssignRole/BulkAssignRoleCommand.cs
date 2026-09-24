using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.AccessControl;
using MediatR;
using Microsoft.EntityFrameworkCore;

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

    public BulkAssignRoleCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<BulkAssignRoleResult> Handle(BulkAssignRoleCommand request, CancellationToken cancellationToken)
    {
        var role = await _context.Roles
            .FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);

        if (role == null)
            throw new InvalidOperationException($"Role with ID {request.RoleId} not found.");

        var userCompanies = await _context.UserCompanies
            .Where(uc => request.UserCompanyIds.Contains(uc.Id))
            .ToListAsync(cancellationToken);

        if (userCompanies.Count != request.UserCompanyIds.Length)
            throw new InvalidOperationException("One or more UserCompany IDs not found.");

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
