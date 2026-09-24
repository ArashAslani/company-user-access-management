using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Personnel.Commands;

public record AssignPositionCommand : IRequest<Guid>
{
    public Guid PersonnelId { get; init; }
    public Guid PositionId { get; init; }
    public bool IsPrimary { get; init; }
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public PersonnelPositionStatus Status { get; init; } = PersonnelPositionStatus.Active;
}

public class AssignPositionCommandHandler : IRequestHandler<AssignPositionCommand, Guid>
{
    private readonly IApplicationDbContext _context;

    public AssignPositionCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Guid> Handle(AssignPositionCommand request, CancellationToken cancellationToken)
    {
        var personnel = await _context.Personnel
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == request.PersonnelId, cancellationToken);

        if (personnel == null)
            throw new InvalidOperationException("Personnel not found.");

        var position = await _context.Positions
            .FirstOrDefaultAsync(p => p.Id == request.PositionId, cancellationToken);

        if (position == null)
            throw new InvalidOperationException("Position not found.");

        // Check for overlap
        if (personnel.Positions.Any(p => p.PositionId == request.PositionId && p.IsCurrentlyEffective()))
            throw new InvalidOperationException("Personnel already has an effective assignment to this position.");

        if (personnel.Positions.Any(p => p.PositionId == request.PositionId && p.HasOverlap(request.EffectiveFrom, request.EffectiveTo)))
            throw new InvalidOperationException("Effective window overlaps with existing assignment for this position.");

        if (request.IsPrimary)
        {
            if (personnel.Positions.Any(p => p.IsPrimary && p.Position?.CompanyId != null && p.IsCurrentlyEffective() && p.HasOverlap(request.EffectiveFrom, request.EffectiveTo)))
                throw new InvalidOperationException("PRIMARY_OVERLAP_CONFLICT");
        }

        personnel.AssignPosition(request.PositionId, request.IsPrimary, request.EffectiveFrom, request.EffectiveTo);

        await _context.SaveChangesAsync(cancellationToken);

        // Return the new PersonnelPosition ID
        var newAssignment = personnel.Positions.First(p => p.PositionId == request.PositionId && p.EffectiveFrom == request.EffectiveFrom);
        return newAssignment.PersonnelId; // The composite key
    }
}
