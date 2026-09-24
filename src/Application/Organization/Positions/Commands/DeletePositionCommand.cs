using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Positions.Commands;

public record DeletePositionCommand : IRequest
{
    public Guid Id { get; init; }
}

public class DeletePositionCommandHandler : IRequestHandler<DeletePositionCommand>
{
    private readonly IApplicationDbContext _context;

    public DeletePositionCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(DeletePositionCommand request, CancellationToken cancellationToken)
    {
        var position = await _context.Positions
            .Include(p => p.Assignments)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (position == null)
            throw new InvalidOperationException("Position not found.");

        // Check for active assignments
        var activeAssignments = position.Assignments
            .Where(a => a.IsCurrentlyEffective())
            .ToList();

        if (activeAssignments.Any())
        {
            var assignmentIds = activeAssignments.Select(a => a.PersonnelId).ToList();
            throw new InvalidOperationException("ACTIVE_ASSIGNMENT_EXISTS")
            {
                Data = { ["activePersonnelPositionIds"] = assignmentIds }
            };
        }

        // Soft delete - deactivate
        position.SetStatus(PositionStatus.Inactive);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
