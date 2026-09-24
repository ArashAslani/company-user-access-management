using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Positions.Commands;

public record CreatePositionCommand : IRequest<Guid>
{
    public string Kind { get; init; } = null!; // "Organizational" | "NonOrganizational"
    public Guid HoldingId { get; init; }
    public Guid CompanyId { get; init; }
    public string Code { get; init; } = null!;
    public string Title { get; init; } = null!;
    public Guid? ParentPositionId { get; init; }
    public PositionStatus Status { get; init; } = PositionStatus.Active;
    public Guid[] AttachmentIds { get; init; } = [];
}

public class CreatePositionCommandHandler : IRequestHandler<CreatePositionCommand, Guid>
{
    private readonly IApplicationDbContext _context;

    public CreatePositionCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Guid> Handle(CreatePositionCommand request, CancellationToken cancellationToken)
    {
        // Validate parent position belongs to same company
        if (request.ParentPositionId.HasValue)
        {
            var parent = await _context.Positions
                .FirstOrDefaultAsync(p => p.Id == request.ParentPositionId.Value, cancellationToken);

            if (parent == null)
                throw new InvalidOperationException("Parent position not found.");

            if (parent.CompanyId != request.CompanyId)
                throw new InvalidOperationException("Parent position must belong to the same company.");

            // Cycle check
            if (await WouldCreateCycle(request.ParentPositionId.Value, request.CompanyId, cancellationToken))
                throw new InvalidOperationException("Position hierarchy cycle detected.");
        }

        // Check unique code per company
        var codeExists = await _context.Positions
            .AnyAsync(p => p.CompanyId == request.CompanyId && p.Code == request.Code, cancellationToken);

        if (codeExists)
            throw new InvalidOperationException("Position code must be unique within the company.");

        var position = new Position(request.CompanyId, request.Code, request.Title, null, request.ParentPositionId);
        position.SetStatus(request.Status);

        _context.Positions.Add(position);
        await _context.SaveChangesAsync(cancellationToken);

        return position.Id;
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
