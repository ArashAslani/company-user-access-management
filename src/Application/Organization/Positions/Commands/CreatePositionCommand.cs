using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Application.Common.Hierarchy;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Application.Common.Validation;
using CompanyAccessManagement.Domain.Common;
using CompanyAccessManagement.Domain.Organization;

namespace CompanyAccessManagement.Application.Organization.Positions.Commands;

public record CreatePositionCommand : IRequest<Guid>, IExternalIdentityFields
{
    public Guid CompanyId { get; init; }
    public string Code { get; init; } = null!;
    public string Title { get; init; } = null!;
    public string? Description { get; init; }
    public Guid? ParentPositionId { get; init; }
    public PositionStatus Status { get; init; } = PositionStatus.Active;
    public string? ExternalSource { get; init; }
    public string? ExternalId { get; init; }
}

public class CreatePositionCommandHandler : IRequestHandler<CreatePositionCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditWriter _audit;

    public CreatePositionCommandHandler(IApplicationDbContext context, ICurrentWorkspace workspace, TimeProvider timeProvider, IAuditWriter audit)
    {
        _context = context;
        _workspace = workspace;
        _timeProvider = timeProvider;
        _audit = audit;
    }

    public async Task<Guid> Handle(CreatePositionCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.EnsureCompany(request.CompanyId);

        if (request.ParentPositionId.HasValue)
        {
            var parent = await _context.Positions
                .FirstOrDefaultAsync(p => p.Id == request.ParentPositionId.Value && p.CompanyId == companyId, cancellationToken);

            Guard.Against.NotFound(request.ParentPositionId.Value, parent);

            await HierarchyCycle.EnsureAcyclicAsync(
                Guid.Empty,
                request.ParentPositionId,
                async (id, ct) => await _context.Positions
                    .Where(p => p.Id == id && p.CompanyId == companyId)
                    .Select(p => p.ParentPositionId)
                    .FirstOrDefaultAsync(ct),
                cancellationToken,
                "Position");
        }

        var codeExists = await _context.Positions
            .AnyAsync(p => p.CompanyId == companyId && p.Code == request.Code, cancellationToken);

        if (codeExists)
            throw new DomainRuleViolationException("POSITION_CODE_DUPLICATE", "Position code must be unique within the company.");

        var (externalSource, externalId) = ExternalIdentity.Normalize(request.ExternalSource, request.ExternalId);
        if (externalSource is not null
            && await _context.Positions.AnyAsync(p => p.CompanyId == companyId && p.ExternalSource == externalSource && p.ExternalId == externalId, cancellationToken))
            throw ExternalIdentityRules.Duplicate("position");

        var position = new Position(companyId, request.Code, request.Title, request.Description, request.ParentPositionId);
        position.SetExternalIdentity(externalSource, externalId);
        position.SetStatus(request.Status, _timeProvider.GetUtcNow().UtcDateTime);

        _context.Positions.Add(position);
        _audit.Write(new AuditWriteRequest(AuditEventTypes.PositionCreated, nameof(Position), position.Id, CompanyId: companyId));
        await _context.SaveChangesAsync(cancellationToken);

        return position.Id;
    }
}
