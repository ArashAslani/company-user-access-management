using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.AccessControl;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.AccessControl.Roles.Commands;

public record UpdateRoleCommand : IRequest
{
    public Guid Id { get; set; }
    public string Name { get; init; } = null!;
    public string Code { get; init; } = null!;
    public string? Description { get; init; }
    public RoleKind Kind { get; init; }
    public Guid? ParentRoleId { get; init; }
    public DateTime? ValidUntil { get; init; }
    public RoleStatus Status { get; init; }
}

public class UpdateRoleCommandHandler : IRequestHandler<UpdateRoleCommand>
{
    private readonly IApplicationDbContext _context;

    public UpdateRoleCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(UpdateRoleCommand request, CancellationToken cancellationToken)
    {
        var role = await _context.Roles
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (role == null)
            throw new InvalidOperationException("Role not found.");

        // Validate parent role
        if (request.ParentRoleId.HasValue)
        {
            if (request.ParentRoleId.Value == request.Id)
                throw new InvalidOperationException("Role cannot be its own parent.");

            var parent = await _context.Roles
                .FirstOrDefaultAsync(r => r.Id == request.ParentRoleId.Value, cancellationToken);

            if (parent == null)
                throw new InvalidOperationException("Parent role not found.");

            if (parent.CompanyId != role.CompanyId)
                throw new InvalidOperationException("Parent role must belong to the same company.");

            if (parent.ApplicationId != role.ApplicationId)
                throw new InvalidOperationException("Parent role must belong to the same application.");

            // Cycle check
            if (await WouldCreateCycle(request.ParentRoleId.Value, role.CompanyId, role.ApplicationId, cancellationToken))
                throw new InvalidOperationException("Role hierarchy cycle detected.");
        }

        // Check unique code per company+application (excluding self)
        var codeExists = await _context.Roles
            .AnyAsync(r => r.CompanyId == role.CompanyId && r.ApplicationId == role.ApplicationId && r.Code == request.Code && r.Id != request.Id, cancellationToken);

        if (codeExists)
            throw new InvalidOperationException("Role code must be unique within the company and application.");

        role.UpdateDetails(request.Name, request.Kind, request.ValidUntil);
        role.UpdateCodeAndDescription(request.Code, request.Description);

        if (request.ParentRoleId != role.ParentRoleId)
        {
            role.ChangeParent(request.ParentRoleId);
        }

        role.SetStatus(request.Status);

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> WouldCreateCycle(Guid parentId, Guid companyId, Guid applicationId, CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid>();
        var current = parentId;

        while (current != Guid.Empty)
        {
            if (visited.Contains(current))
                return true;

            visited.Add(current);

            var role = await _context.Roles
                .FirstOrDefaultAsync(r => r.Id == current, cancellationToken);

            if (role == null || role.CompanyId != companyId || role.ApplicationId != applicationId)
                break;

            current = role.ParentRoleId ?? Guid.Empty;
        }

        return false;
    }
}
