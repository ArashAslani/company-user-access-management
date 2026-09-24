using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Personnel.Commands;

public record RemovePositionAssignmentCommand : IRequest
{
    public Guid PersonnelId { get; init; }
    public Guid PositionId { get; init; }
}

public class RemovePositionAssignmentCommandHandler : IRequestHandler<RemovePositionAssignmentCommand>
{
    private readonly IApplicationDbContext _context;

    public RemovePositionAssignmentCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(RemovePositionAssignmentCommand request, CancellationToken cancellationToken)
    {
        var personnel = await _context.Personnel
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == request.PersonnelId, cancellationToken);

        if (personnel == null)
            throw new InvalidOperationException("Personnel not found.");

        // Find the assignment by PersonnelId + PositionId (composite key)
        var assignment = personnel.Positions.FirstOrDefault(p => p.PositionId == request.PositionId);
        if (assignment == null)
            throw new InvalidOperationException("Position assignment not found.");

        // Check if sealed
        var now = DateTime.UtcNow;
        if (now > (assignment.EffectiveTo ?? DateTime.MaxValue))
            throw new InvalidOperationException("SEALED_RECORD");

        // Soft delete
        assignment.SetStatus(PersonnelPositionStatus.Inactive, now);

        // If this was the primary and there are no more effective positions, revert to Draft
        if (!personnel.Positions.Any(p => p.IsCurrentlyEffective()))
            personnel.SetStatus(PersonnelStatus.Draft);

        await _context.SaveChangesAsync(cancellationToken);
    }
}