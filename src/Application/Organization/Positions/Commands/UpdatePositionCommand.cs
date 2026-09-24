using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Positions.Commands;

public record UpdatePositionCommand : IRequest
{
    public Guid Id { get; set; }
    public string Code { get; init; } = null!;
    public string Title { get; init; } = null!;
    public string? Description { get; init; }
    public Guid? ParentPositionId { get; init; }
    public PositionStatus Status { get; init; }
}

public class UpdatePositionCommandHandler : IRequestHandler<UpdatePositionCommand>
{
    private readonly IApplicationDbContext _context;

    public UpdatePositionCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(UpdatePositionCommand request, CancellationToken cancellationToken)
    {
        var position = await _context.Positions
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (position == null)
            throw new InvalidOperationException("Position not found.");

        // Validate parent position
        if (request.ParentPositionId.HasValue)
        {
            if (request.ParentPositionId.Value == request.Id)
                throw new InvalidOperationException("Position cannot be its own parent.");

            var parent = await _context.Positions
                .FirstOrDefaultAsync(p => p.Id == request.ParentPositionId.Value, cancellationToken);

            if (parent == null)
                throw new InvalidOperationException("Parent position not found.");

            if (parent.CompanyId != position.CompanyId)
                throw new InvalidOperationException("Parent position must belong to the same company.");

            // Cycle check
            if (await WouldCreateCycle(request.ParentPositionId.Value, position.CompanyId, cancellationToken))
                throw new InvalidOperationException("Position hierarchy cycle detected.");
        }

        // Check unique code per company (excluding self)
        var codeExists = await _context.Positions
            .AnyAsync(p => p.CompanyId == position.CompanyId && p.Code == request.Code && p.Id != request.Id, cancellationToken);

        if (codeExists)
            throw new InvalidOperationException("Position code must be unique within the company.");

        position.UpdateDetails(request.Code, request.Title, request.Description);

        if (request.ParentPositionId != position.ParentPositionId)
        {
            position.ChangeParent(request.ParentPositionId);
        }

        position.SetStatus(request.Status);

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> WouldCreateCycle(Guid parentId, Guid companyId, CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid>();
        var current = parentId;

        while (current != Guid.Empty)
        {
            if (visited.Contains(current))
                return true;

            visited.Add(current);

            var position = await _context.Positions
                .FirstOrDefaultAsync(p => p.Id == current, cancellationToken);

            if (position == null || position.CompanyId != companyId)
                break;

            current = position.ParentPositionId ?? Guid.Empty;
        }

        return false;
    }
}
