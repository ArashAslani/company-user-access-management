using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.Organization.Personnel.Commands;

public record UpdatePositionAssignmentCommand : IRequest
{
    public Guid PersonnelId { get; set; }
    public Guid PersonnelPositionId { get; init; }
    public bool IsPrimary { get; init; }
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public PersonnelPositionStatus Status { get; init; }
}

public class UpdatePositionAssignmentCommandHandler : IRequestHandler<UpdatePositionAssignmentCommand>
{
    private readonly IApplicationDbContext _context;

    public UpdatePositionAssignmentCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(UpdatePositionAssignmentCommand request, CancellationToken cancellationToken)
    {
        var personnel = await _context.Personnel
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == request.PersonnelId, cancellationToken);

        if (personnel == null)
            throw new InvalidOperationException("Personnel not found.");

        var assignment = personnel.Positions.FirstOrDefault(p => p.PositionId == request.PersonnelPositionId);
        if (assignment == null)
            throw new InvalidOperationException("Position assignment not found.");

        // Check for overlap
        if (personnel.Positions.Any(p => p.PositionId == assignment.PositionId && p.PersonnelId != assignment.PersonnelId && p.HasOverlap(request.EffectiveFrom, request.EffectiveTo)))
            throw new InvalidOperationException("Effective window overlaps with existing assignment for this position.");

        if (request.IsPrimary)
        {
            if (personnel.Positions.Any(p => p.IsPrimary && p.Position?.CompanyId != null && p.IsCurrentlyEffective() && p.HasOverlap(request.EffectiveFrom, request.EffectiveTo)))
                throw new InvalidOperationException("PRIMARY_OVERLAP_CONFLICT");
        }

        // Check if sealed
        var now = DateTime.UtcNow;
        if (now > (assignment.EffectiveTo ?? DateTime.MaxValue))
            throw new InvalidOperationException("SEALED_RECORD");

        personnel.UpdatePositionEffectiveWindow(request.PersonnelPositionId, request.EffectiveFrom, request.EffectiveTo);
        
        if (request.IsPrimary != assignment.IsPrimary)
            personnel.SetPrimaryPosition(assignment.PositionId);

        assignment.SetStatus(request.Status);

        await _context.SaveChangesAsync(cancellationToken);
    }
}