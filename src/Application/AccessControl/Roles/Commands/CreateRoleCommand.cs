using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.AccessControl;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.AccessControl.Roles.Commands;

public record CreateRoleCommand : IRequest<Guid>
{
    public Guid CompanyId { get; init; }
    public Guid ApplicationId { get; init; }
    public string Name { get; init; } = null!;
    public string Code { get; init; } = null!;
    public string? Description { get; init; }
    public Guid? ParentRoleId { get; init; }
    public DateTime? ValidFrom { get; init; }
    public DateTime? ValidUntil { get; init; }
    public RoleKind Kind { get; init; } = RoleKind.Standard;
    public RoleStatus Status { get; init; } = RoleStatus.Active;
    public Guid[] AttachmentIds { get; init; } = [];
}

public class CreateRoleCommandHandler : IRequestHandler<CreateRoleCommand, Guid>
{
    private readonly IApplicationDbContext _context;

    public CreateRoleCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Guid> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        // Validate parent role belongs to same company and application
        if (request.ParentRoleId.HasValue)
        {
            var parent = await _context.Roles
                .FirstOrDefaultAsync(r => r.Id == request.ParentRoleId.Value, cancellationToken);

            if (parent == null)
                throw new InvalidOperationException("Parent role not found.");

            if (parent.CompanyId != request.CompanyId)
                throw new InvalidOperationException("Parent role must belong to the same company.");

            if (parent.ApplicationId != request.ApplicationId)
                throw new InvalidOperationException("Parent role must belong to the same application.");

            // Cycle check
            if (await WouldCreateCycle(request.ParentRoleId.Value, request.CompanyId, request.ApplicationId, cancellationToken))
                throw new InvalidOperationException("Role hierarchy cycle detected.");
        }

        // Check unique code per company+application
        var codeExists = await _context.Roles
            .AnyAsync(r => r.CompanyId == request.CompanyId && r.ApplicationId == request.ApplicationId && r.Code == request.Code, cancellationToken);

        if (codeExists)
            throw new InvalidOperationException("Role code must be unique within the company and application.");

        var role = new Role(request.CompanyId, request.ApplicationId, request.Code, request.Name, request.Kind, request.ParentRoleId, request.ValidUntil);

        role.SetStatus(request.Status);

        _context.Roles.Add(role);
        await _context.SaveChangesAsync(cancellationToken);

        return role.Id;
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