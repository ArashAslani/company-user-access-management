using CompanyAccessManagement.Application.Common.Hierarchy;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Application.Common.Validation;
using CompanyAccessManagement.Domain.Common;
using CompanyAccessManagement.Domain.Organization;

namespace CompanyAccessManagement.Application.Organization.Positions.Commands;

public record UpdatePositionCommand : IRequest, IExternalIdentityFields
{
    public Guid Id { get; set; }
    public string Code { get; init; } = null!;
    public string Title { get; init; } = null!;
    public string? Description { get; init; }
    public Guid? ParentPositionId { get; init; }
    public PositionStatus Status { get; init; }
    public string? ExternalSource { get; init; }
    public string? ExternalId { get; init; }
}

public class UpdatePositionCommandHandler : IRequestHandler<UpdatePositionCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;
    private readonly TimeProvider _timeProvider;

    public UpdatePositionCommandHandler(IApplicationDbContext context, ICurrentWorkspace workspace, TimeProvider timeProvider)
    {
        _context = context;
        _workspace = workspace;
        _timeProvider = timeProvider;
    }

    public async Task Handle(UpdatePositionCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var position = await _context.Positions
            .Include(p => p.Assignments)
            .FirstOrDefaultAsync(p => p.Id == request.Id && p.CompanyId == companyId, cancellationToken);

        Guard.Against.NotFound(request.Id, position);

        if (request.ParentPositionId.HasValue)
        {
            var parent = await _context.Positions
                .FirstOrDefaultAsync(p => p.Id == request.ParentPositionId.Value && p.CompanyId == companyId, cancellationToken);

            Guard.Against.NotFound(request.ParentPositionId.Value, parent);

            await HierarchyCycle.EnsureAcyclicAsync(
                position.Id,
                request.ParentPositionId,
                async (id, ct) => await _context.Positions
                    .Where(p => p.Id == id && p.CompanyId == position.CompanyId)
                    .Select(p => p.ParentPositionId)
                    .FirstOrDefaultAsync(ct),
                cancellationToken,
                "Position");
        }

        var codeExists = await _context.Positions
            .AnyAsync(p => p.CompanyId == position.CompanyId && p.Code == request.Code && p.Id != request.Id, cancellationToken);

        if (codeExists)
            throw new DomainRuleViolationException("POSITION_CODE_DUPLICATE", "Position code must be unique within the company.");

        var (externalSource, externalId) = ExternalIdentity.Normalize(request.ExternalSource, request.ExternalId);
        if (externalSource is not null
            && await _context.Positions.AnyAsync(p => p.CompanyId == position.CompanyId && p.Id != position.Id && p.ExternalSource == externalSource && p.ExternalId == externalId, cancellationToken))
            throw ExternalIdentityRules.Duplicate("position");

        position.UpdateDetails(request.Code, request.Title, request.Description);
        position.SetExternalIdentity(externalSource, externalId);

        if (request.ParentPositionId != position.ParentPositionId)
            position.ChangeParent(request.ParentPositionId);

        position.SetStatus(request.Status, _timeProvider.GetUtcNow().UtcDateTime);

        await _context.SaveChangesAsync(cancellationToken);
    }
}
