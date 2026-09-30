using CompanyAccessManagement.Application.Common.Hierarchy;
using CompanyAccessManagement.Application.Common.Interfaces;
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
    public Guid[] AttachmentIds { get; init; } = [];
}

public class CreatePositionCommandHandler : IRequestHandler<CreatePositionCommand, Guid>
{
    private readonly IApplicationDbContext _context;

    public CreatePositionCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Guid> Handle(CreatePositionCommand request, CancellationToken cancellationToken)
    {
        if (request.ParentPositionId.HasValue)
        {
            var parent = await _context.Positions
                .FirstOrDefaultAsync(p => p.Id == request.ParentPositionId.Value, cancellationToken);

            Guard.Against.NotFound(request.ParentPositionId.Value, parent);

            if (parent.CompanyId != request.CompanyId)
                throw new DomainRuleViolationException("POSITION_PARENT_COMPANY_MISMATCH", "Parent position must belong to the same company.");

            await HierarchyCycle.EnsureAcyclicAsync(
                Guid.Empty,
                request.ParentPositionId,
                async (id, ct) => await _context.Positions
                    .Where(p => p.Id == id && p.CompanyId == request.CompanyId)
                    .Select(p => p.ParentPositionId)
                    .FirstOrDefaultAsync(ct),
                cancellationToken,
                "Position");
        }

        var codeExists = await _context.Positions
            .AnyAsync(p => p.CompanyId == request.CompanyId && p.Code == request.Code, cancellationToken);

        if (codeExists)
            throw new DomainRuleViolationException("POSITION_CODE_DUPLICATE", "Position code must be unique within the company.");

        var position = new Position(request.CompanyId, request.Code, request.Title, null, request.ParentPositionId);
        position.SetStatus(request.Status);

        _context.Positions.Add(position);
        await _context.SaveChangesAsync(cancellationToken);

        return position.Id;
    }
}
