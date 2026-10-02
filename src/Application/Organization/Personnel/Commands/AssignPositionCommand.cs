using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Application.Common.Validation;
using CompanyAccessManagement.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Personnel.Commands;

public record AssignPositionCommand : IRequest<Guid>, IExternalIdentityFields
{
    public Guid PersonnelId { get; init; }
    public Guid PositionId { get; init; }
    public bool IsPrimary { get; init; }
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public string? ExternalSource { get; init; }
    public string? ExternalId { get; init; }
}

public class AssignPositionCommandHandler : IRequestHandler<AssignPositionCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentWorkspace _workspace;
    private readonly IAuditWriter _audit;

    public AssignPositionCommandHandler(IApplicationDbContext context, TimeProvider timeProvider, ICurrentWorkspace workspace, IAuditWriter audit)
    {
        _context = context;
        _timeProvider = timeProvider;
        _workspace = workspace;
        _audit = audit;
    }

    public async Task<Guid> Handle(AssignPositionCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        if (!await PersonnelWorkspaceScope.IsPositionInCompanyAsync(_context, request.PositionId, companyId, cancellationToken))
            throw new NotFoundException(request.PositionId.ToString(), "Position");

        await PositionCompanyLookup.EnsurePositionActiveAsync(_context, request.PositionId, cancellationToken);

        var personnel = await _context.Personnel
            .VisibleIn(companyId)
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == request.PersonnelId, cancellationToken);

        Guard.Against.NotFound(request.PersonnelId, personnel);

        var positionCompanies = await PositionCompanyLookup.LoadAsync(_context, personnel, request.PositionId, cancellationToken);
        if (!positionCompanies.ContainsKey(request.PositionId))
            throw new NotFoundException(request.PositionId.ToString(), "Position");

        var (externalSource, externalId) = ExternalIdentity.Normalize(request.ExternalSource, request.ExternalId);
        if (externalSource is not null && personnel.Positions.Any(pp => pp.ExternalSource == externalSource && pp.ExternalId == externalId))
            throw ExternalIdentityRules.Duplicate("position assignment");

        var assignment = personnel.AssignPosition(
            request.PositionId,
            request.IsPrimary,
            request.EffectiveFrom,
            request.EffectiveTo,
            _timeProvider.GetUtcNow().UtcDateTime,
            positionCompanies);
        assignment.SetExternalIdentity(externalSource, externalId);

        var afterData = AuditJson.Serialize(new
        {
            personnelId = personnel.Id,
            assignment.PositionId,
            assignment.EffectiveFrom,
            assignment.EffectiveTo,
            assignment.IsPrimary
        });
        _audit.Write(new AuditWriteRequest(AuditEventTypes.PersonnelPositionAssigned, "PersonnelPosition", assignment.Id,
            AfterData: afterData, CompanyId: companyId));
        if (assignment.IsPrimary)
            _audit.Write(new AuditWriteRequest(AuditEventTypes.PrimaryPositionChanged, "PersonnelPosition", assignment.Id,
                AfterData: afterData, CompanyId: companyId));
        await _context.SaveChangesAsync(cancellationToken);

        return assignment.Id;
    }
}
