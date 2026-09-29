using CompanyAccessManagement.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Personnel.Commands;

public record RemovePositionAssignmentCommand : IRequest
{
    public Guid PersonnelId { get; init; }
    public Guid AssignmentId { get; init; }
}

public class RemovePositionAssignmentCommandHandler : IRequestHandler<RemovePositionAssignmentCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public RemovePositionAssignmentCommandHandler(IApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task Handle(RemovePositionAssignmentCommand request, CancellationToken cancellationToken)
    {
        var personnel = await _context.Personnel
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == request.PersonnelId, cancellationToken);

        Guard.Against.NotFound(request.PersonnelId, personnel);

        if (personnel.FindAssignment(request.AssignmentId) is null)
            throw new NotFoundException(request.AssignmentId.ToString(), "PersonnelPosition");

        personnel.RemovePositionAssignment(request.AssignmentId, _timeProvider.GetUtcNow().UtcDateTime);

        await _context.SaveChangesAsync(cancellationToken);
    }
}
