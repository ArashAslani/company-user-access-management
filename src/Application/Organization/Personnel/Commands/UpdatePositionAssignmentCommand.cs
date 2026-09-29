using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Personnel.Commands;

public record UpdatePositionAssignmentCommand : IRequest
{
    public Guid PersonnelId { get; init; }
    public Guid AssignmentId { get; init; }
    public bool IsPrimary { get; init; }
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public PersonnelPositionStatus Status { get; init; } = PersonnelPositionStatus.Active;
}

public class UpdatePositionAssignmentCommandHandler : IRequestHandler<UpdatePositionAssignmentCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public UpdatePositionAssignmentCommandHandler(IApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task Handle(UpdatePositionAssignmentCommand request, CancellationToken cancellationToken)
    {
        var personnel = await _context.Personnel
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == request.PersonnelId, cancellationToken);

        Guard.Against.NotFound(request.PersonnelId, personnel);

        if (personnel.FindAssignment(request.AssignmentId) is null)
            throw new NotFoundException(request.AssignmentId.ToString(), "PersonnelPosition");

        personnel.UpdatePositionAssignment(
            request.AssignmentId,
            request.IsPrimary,
            request.EffectiveFrom,
            request.EffectiveTo,
            request.Status,
            _timeProvider.GetUtcNow().UtcDateTime);

        await _context.SaveChangesAsync(cancellationToken);
    }
}
