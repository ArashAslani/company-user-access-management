using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
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
    private readonly ICurrentWorkspace _workspace;
    private readonly IAuditWriter _audit;

    public RemovePositionAssignmentCommandHandler(IApplicationDbContext context, TimeProvider timeProvider, ICurrentWorkspace workspace, IAuditWriter audit)
    {
        _context = context;
        _timeProvider = timeProvider;
        _workspace = workspace;
        _audit = audit;
    }

    public async Task Handle(RemovePositionAssignmentCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var personnel = await _context.Personnel
            .VisibleIn(companyId)
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == request.PersonnelId, cancellationToken);

        Guard.Against.NotFound(request.PersonnelId, personnel);

        var assignment = personnel.FindAssignment(request.AssignmentId);
        if (assignment is null || !await PersonnelWorkspaceScope.IsPositionInCompanyAsync(_context, assignment.PositionId, companyId, cancellationToken))
            throw new NotFoundException(request.AssignmentId.ToString(), "PersonnelPosition");

        personnel.RemovePositionAssignment(request.AssignmentId, _timeProvider.GetUtcNow().UtcDateTime);

        _audit.Write(new AuditWriteRequest(AuditEventTypes.PersonnelPositionRemoved, "PersonnelPosition", assignment.Id,
            BeforeData: AuditJson.Serialize(new
            {
                personnelId = personnel.Id,
                assignment.PositionId,
                assignment.EffectiveFrom,
                assignment.EffectiveTo,
                assignment.IsPrimary
            }),
            CompanyId: companyId));
        await _context.SaveChangesAsync(cancellationToken);
    }
}
