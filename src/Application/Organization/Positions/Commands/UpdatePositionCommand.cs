using CompanyAccessManagement.Application.Common.Hierarchy;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.Common;
using CompanyAccessManagement.Domain.Organization;

namespace CompanyAccessManagement.Application.Organization.Positions.Commands;

public record UpdatePositionCommand : IRequest
{
    public Guid Id { get; set; }
    public string Code { get; init; } = null!;
    public string Title { get; init; } = null!;
    public string? Description { get; init; }
    public Guid? ParentPositionId { get; init; }
    public PositionStatus Status { get; init; }
}

public class UpdatePositionCommandHandler : IRequestHandler<UpdatePositionCommand>
{
    private readonly IApplicationDbContext _context;

    public UpdatePositionCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(UpdatePositionCommand request, CancellationToken cancellationToken)
    {
        var position = await _context.Positions
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        Guard.Against.NotFound(request.Id, position);

        if (request.ParentPositionId.HasValue)
        {
            var parent = await _context.Positions
                .FirstOrDefaultAsync(p => p.Id == request.ParentPositionId.Value, cancellationToken);

            Guard.Against.NotFound(request.ParentPositionId.Value, parent);

            if (parent.CompanyId != position.CompanyId)
                throw new DomainRuleViolationException("POSITION_PARENT_COMPANY_MISMATCH", "Parent position must belong to the same company.");

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

        position.UpdateDetails(request.Code, request.Title, request.Description);

        if (request.ParentPositionId != position.ParentPositionId)
            position.ChangeParent(request.ParentPositionId);

        position.SetStatus(request.Status);

        await _context.SaveChangesAsync(cancellationToken);
    }
}
