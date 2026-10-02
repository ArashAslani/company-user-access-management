using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Application.Common.Validation;
using CompanyAccessManagement.Domain.Common;
using CompanyAccessManagement.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Personnel.Commands;

public record UpdatePositionAssignmentCommand : IRequest, IExternalIdentityFields
{
    public Guid PersonnelId { get; init; }
    public Guid AssignmentId { get; init; }
    public bool IsPrimary { get; init; }
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public PersonnelPositionStatus Status { get; init; } = PersonnelPositionStatus.Active;
    public string? ExternalSource { get; init; }
    public string? ExternalId { get; init; }
}

public class UpdatePositionAssignmentCommandHandler : IRequestHandler<UpdatePositionAssignmentCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentWorkspace _workspace;
    private readonly IAuditWriter _audit;

    public UpdatePositionAssignmentCommandHandler(IApplicationDbContext context, TimeProvider timeProvider, ICurrentWorkspace workspace, IAuditWriter audit)
    {
        _context = context;
        _timeProvider = timeProvider;
        _workspace = workspace;
        _audit = audit;
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
        var wasPrimary = assignment.IsPrimary;

        if (!await PersonnelWorkspaceScope.IsPositionInCompanyAsync(_context, assignment.PositionId, companyId, cancellationToken))
            throw new NotFoundException(request.AssignmentId.ToString(), "PersonnelPosition");

        if (request.Status == PersonnelPositionStatus.Active)
            await PositionCompanyLookup.EnsurePositionActiveAsync(_context, assignment.PositionId, cancellationToken);

        var (externalSource, externalId) = ExternalIdentity.Normalize(request.ExternalSource, request.ExternalId);
        if (externalSource is not null && personnel.Positions.Any(pp => pp.Id != assignment.Id && pp.ExternalSource == externalSource && pp.ExternalId == externalId))
            throw ExternalIdentityRules.Duplicate("position assignment");

        var positionCompanies = await PositionCompanyLookup.LoadAsync(_context, personnel, assignment.PositionId, cancellationToken);

        personnel.UpdatePositionAssignment(
            request.AssignmentId,
            request.IsPrimary,
            request.EffectiveFrom,
            request.EffectiveTo,
            request.Status,
            _timeProvider.GetUtcNow().UtcDateTime,
            positionCompanies);
        assignment.SetExternalIdentity(externalSource, externalId);

        if (wasPrimary != assignment.IsPrimary)
            _audit.Write(new AuditWriteRequest(AuditEventTypes.PrimaryPositionChanged, nameof(PersonnelPosition), assignment.Id, CompanyId: companyId));
        await _context.SaveChangesAsync(cancellationToken);
    }
}
