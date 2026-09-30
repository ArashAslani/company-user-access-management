using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
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
    private readonly ICurrentWorkspace _workspace;

    public UpdatePositionAssignmentCommandHandler(IApplicationDbContext context, TimeProvider timeProvider, ICurrentWorkspace workspace)
    {
        _context = context;
        _timeProvider = timeProvider;
        _workspace = workspace;
    }

    public async Task Handle(UpdatePositionAssignmentCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var personnel = await _context.Personnel
            .VisibleIn(companyId)
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == request.PersonnelId, cancellationToken);

        Guard.Against.NotFound(request.PersonnelId, personnel);

        var assignment = personnel.FindAssignment(request.AssignmentId)
            ?? throw new NotFoundException(request.AssignmentId.ToString(), "PersonnelPosition");

        if (!await PersonnelWorkspaceScope.IsPositionInCompanyAsync(_context, assignment.PositionId, companyId, cancellationToken))
            throw new NotFoundException(request.AssignmentId.ToString(), "PersonnelPosition");

        if (request.Status == PersonnelPositionStatus.Active)
            await PositionCompanyLookup.EnsurePositionActiveAsync(_context, assignment.PositionId, cancellationToken);

        var positionCompanies = await PositionCompanyLookup.LoadAsync(_context, personnel, assignment.PositionId, cancellationToken);

        personnel.UpdatePositionAssignment(
            request.AssignmentId,
            request.IsPrimary,
            request.EffectiveFrom,
            request.EffectiveTo,
            request.Status,
            _timeProvider.GetUtcNow().UtcDateTime,
            positionCompanies);

        await _context.SaveChangesAsync(cancellationToken);
    }
}
