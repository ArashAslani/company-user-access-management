using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Organization.Positions.Queries;
using CompanyAccessManagement.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Positions.Queries;

public record GetPositionTreeQuery : IRequest<PositionTreeDto>
{
    public Guid HoldingId { get; init; }
}

public class GetPositionTreeQueryHandler : IRequestHandler<GetPositionTreeQuery, PositionTreeDto>
{
    private readonly IApplicationDbContext _context;

    public GetPositionTreeQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PositionTreeDto> Handle(GetPositionTreeQuery request, CancellationToken cancellationToken)
    {
        // Get all companies under the holding
        var companies = await _context.Companies
            .Where(c => c.ParentCompanyId == request.HoldingId || c.Id == request.HoldingId)
            .ToListAsync(cancellationToken);

        var holding = await _context.Companies
            .FirstOrDefaultAsync(c => c.Id == request.HoldingId, cancellationToken);

        var tree = new PositionTreeDto
        {
            HoldingId = request.HoldingId,
            HoldingName = holding?.Name ?? "Holding",
            Companies = new List<CompanyTreeDto>()
        };

        foreach (var company in companies)
        {
            var rootPositions = await _context.Positions
                .Where(p => p.CompanyId == company.Id && p.ParentPositionId == null)
                .ToListAsync(cancellationToken);

            var companyTree = new CompanyTreeDto
            {
                Id = company.Id,
                Name = company.Name,
                Positions = new List<PositionTreeItemDto>()
            };

            foreach (var root in rootPositions)
            {
                companyTree.Positions.Add(await BuildPositionTreeItem(root, cancellationToken));
            }

            tree.Companies.Add(companyTree);
        }

        return tree;
    }

    private async Task<PositionTreeItemDto> BuildPositionTreeItem(Position position, CancellationToken cancellationToken)
    {
        var children = await _context.Positions
            .Where(p => p.ParentPositionId == position.Id)
            .ToListAsync(cancellationToken);

        var item = new PositionTreeItemDto
        {
            Id = position.Id,
            Code = position.Code,
            Title = position.Title,
            ParentPositionId = position.ParentPositionId,
            Children = new List<PositionTreeItemDto>()
        };

        foreach (var child in children)
        {
            item.Children.Add(await BuildPositionTreeItem(child, cancellationToken));
        }

        return item;
    }
}
