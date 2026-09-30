using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
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
}

public class AssignPositionCommandHandler : IRequestHandler<AssignPositionCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentWorkspace _workspace;

    public AssignPositionCommandHandler(IApplicationDbContext context, TimeProvider timeProvider, ICurrentWorkspace workspace)
    {
        _context = context;
        _timeProvider = timeProvider;
        _workspace = workspace;
    }

    public async Task<Guid> Handle(AssignPositionCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        if (!await PersonnelWorkspaceScope.IsPositionInCompanyAsync(_context, request.PositionId, companyId, cancellationToken))
            throw new NotFoundException(request.PositionId.ToString(), "Position");

        await PositionCompanyLookup.EnsurePositionActiveAsync(_context, request.PositionId, cancellationToken);

        var personnel = await _context.Personnel
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == request.PersonnelId, cancellationToken);

        Guard.Against.NotFound(request.PersonnelId, personnel);

        var positionCompanies = await PositionCompanyLookup.LoadAsync(_context, personnel, request.PositionId, cancellationToken);
        if (!positionCompanies.ContainsKey(request.PositionId))
            throw new NotFoundException(request.PositionId.ToString(), "Position");

        var assignment = personnel.AssignPosition(
            request.PositionId,
            request.IsPrimary,
            request.EffectiveFrom,
            request.EffectiveTo,
            _timeProvider.GetUtcNow().UtcDateTime,
            positionCompanies);

        await _context.SaveChangesAsync(cancellationToken);

        return assignment.Id;
    }
}
