using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.AccessControl;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.AccessControl.Roles.Commands;

public record DeleteRoleCommand : IRequest
{
    public Guid Id { get; init; }
}

public class DeleteRoleCommandHandler : IRequestHandler<DeleteRoleCommand>
{
    private readonly IApplicationDbContext _context;

    public DeleteRoleCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        var role = await _context.Roles
            .Include(r => r.UserRoles)
            .Include(r => r.AccessRules)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (role == null)
            throw new InvalidOperationException("Role not found.");

        if (role.UserRoles.Any() || role.AccessRules.Any())
            throw new InvalidOperationException("Cannot delete role with active assignments or permissions.");

        // Soft delete - deactivate
        role.SetStatus(RoleStatus.Inactive);
        await _context.SaveChangesAsync(cancellationToken);
    }
}