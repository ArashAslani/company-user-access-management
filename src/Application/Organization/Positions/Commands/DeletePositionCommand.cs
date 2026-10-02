using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
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
    private readonly ICurrentWorkspace _workspace;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditWriter _audit;

    public DeletePositionCommandHandler(IApplicationDbContext context, ICurrentWorkspace workspace, TimeProvider timeProvider, IAuditWriter audit)
    {
        _context = context;
        _workspace = workspace;
        _timeProvider = timeProvider;
        _audit = audit;
    }

    public async Task Handle(DeletePositionCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var position = await _context.Positions
            .Include(p => p.Assignments)
            .FirstOrDefaultAsync(p => p.Id == request.Id && p.CompanyId == companyId, cancellationToken);

        Guard.Against.NotFound(request.Id, position);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var activeAssignments = position.Assignments
            .Where(a => a.IsCurrentlyEffective(now))
            .ToList();

        if (activeAssignments.Any())
        {
            var assignmentIds = activeAssignments.Select(a => a.Id).ToList();
            throw new ConflictException("Cannot delete position with active personnel assignments.")
            {
                Data = { ["activePersonnelPositionIds"] = assignmentIds }
            };
        }

        // Soft delete - deactivate
        position.SetStatus(PositionStatus.Inactive, now);
        _audit.Write(new AuditWriteRequest(AuditEventTypes.PositionDeactivated, nameof(Position), position.Id, CompanyId: companyId));
        await _context.SaveChangesAsync(cancellationToken);
    }
}
