using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.Organization;

namespace CompanyAccessManagement.Application.Organization.Personnel.Commands;

public record CorrectPositionAssignmentCommand : IRequest<Guid>
{
    public Guid PersonnelId { get; init; }
    public Guid AssignmentId { get; init; }
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public bool IsPrimary { get; init; }
    public string Reason { get; init; } = null!;
}

public sealed class CorrectPositionAssignmentCommandValidator : AbstractValidator<CorrectPositionAssignmentCommand>
{
    public CorrectPositionAssignmentCommandValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.EffectiveTo).GreaterThan(x => x.EffectiveFrom).When(x => x.EffectiveTo.HasValue);
    }
}

public sealed class CorrectPositionAssignmentCommandHandler : IRequestHandler<CorrectPositionAssignmentCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditWriter _audit;

    public CorrectPositionAssignmentCommandHandler(
        IApplicationDbContext context,
        ICurrentWorkspace workspace,
        TimeProvider timeProvider,
        IAuditWriter audit)
    {
        _context = context;
        _workspace = workspace;
        _timeProvider = timeProvider;
        _audit = audit;
    }

    public async Task<Guid> Handle(CorrectPositionAssignmentCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();
        var personnel = await _context.Personnel
            .VisibleIn(companyId)
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == request.PersonnelId, cancellationToken);
        Guard.Against.NotFound(request.PersonnelId, personnel);

        var old = personnel.FindAssignment(request.AssignmentId)
            ?? throw new NotFoundException(request.AssignmentId.ToString(), nameof(PersonnelPosition));
        if (!await PersonnelWorkspaceScope.IsPositionInCompanyAsync(_context, old.PositionId, companyId, cancellationToken))
            throw new NotFoundException(request.AssignmentId.ToString(), nameof(PersonnelPosition));

        var positionCompanies = await PositionCompanyLookup.LoadAsync(_context, personnel, old.PositionId, cancellationToken);
        var correction = personnel.CorrectPositionAssignment(
            request.AssignmentId,
            request.EffectiveFrom,
            request.EffectiveTo,
            request.IsPrimary,
            _timeProvider.GetUtcNow().UtcDateTime,
            positionCompanies);

        _audit.Write(new AuditWriteRequest(
            AuditEventTypes.PersonnelPositionCorrected,
            nameof(PersonnelPosition),
            correction.Id,
            BeforeData: AuditJson.Serialize(new
            {
                old.Id,
                old.EffectiveFrom,
                old.EffectiveTo,
                old.IsPrimary
            }),
            AfterData: AuditJson.Serialize(new
            {
                correction.Id,
                correction.EffectiveFrom,
                correction.EffectiveTo,
                correction.IsPrimary,
                correction.CorrectsAssignmentId
            }),
            Metadata: AuditJson.Serialize(new { request.Reason }),
            CompanyId: companyId));

        await _context.SaveChangesAsync(cancellationToken);
        return correction.Id;
    }
}
