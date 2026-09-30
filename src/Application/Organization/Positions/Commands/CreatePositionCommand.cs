using CompanyAccessManagement.Application.Common.Hierarchy;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.Common;
using CompanyAccessManagement.Domain.Organization;

namespace CompanyAccessManagement.Application.Organization.Positions.Commands;

public record CreatePositionCommand : IRequest<Guid>
{
    public string Kind { get; init; } = null!; // "Organizational" | "NonOrganizational"
    public Guid HoldingId { get; init; }
    public Guid CompanyId { get; init; }
    public string Code { get; init; } = null!;
    public string Title { get; init; } = null!;
    public Guid? ParentPositionId { get; init; }
    public PositionStatus Status { get; init; } = PositionStatus.Active;
}

public class CreatePositionCommandHandler : IRequestHandler<CreatePositionCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;
    private readonly TimeProvider _timeProvider;

    public CreatePositionCommandHandler(IApplicationDbContext context, ICurrentWorkspace workspace, TimeProvider timeProvider)
    {
        _context = context;
        _workspace = workspace;
        _timeProvider = timeProvider;
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

        var position = new Position(companyId, request.Code, request.Title, null, request.ParentPositionId);
        position.SetStatus(request.Status, _timeProvider.GetUtcNow().UtcDateTime);

        _context.Positions.Add(position);
        await _context.SaveChangesAsync(cancellationToken);

        return position.Id;
    }
}
