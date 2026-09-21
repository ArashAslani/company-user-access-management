using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Organization.Positions.Queries;
using CleanArchitecture.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.Organization.Positions.Queries;

public record GetPositionSummaryQuery : IRequest<PositionSummaryDto?>
{
    public Guid Id { get; init; }
}

public class GetPositionSummaryQueryHandler : IRequestHandler<GetPositionSummaryQuery, PositionSummaryDto?>
{
    private readonly IApplicationDbContext _context;

    public GetPositionSummaryQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PositionSummaryDto?> Handle(GetPositionSummaryQuery request, CancellationToken cancellationToken)
    {
        var position = await _context.Positions
            .Include(p => p.Assignments)
                .ThenInclude(pp => pp.Personnel)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (position == null)
            return null;

        return new PositionSummaryDto
        {
            Id = position.Id,
            Code = position.Code,
            Title = position.Title,
            Status = position.Status,
            Description = position.Description,
            Personnel = position.Assignments
                .Where(a => a.IsCurrentlyEffective())
                .Select(a => new PersonnelSummaryDto
                {
                    PersonnelId = a.PersonnelId,
                    FullName = $"{a.Personnel?.FirstName} {a.Personnel?.LastName}"
                }).ToList()
        };
    }
}