using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;

namespace CompanyAccessManagement.Application.Organization.Positions.Queries;

public record GetPositionTreeQuery : IRequest<PositionTreeDto>
{
    public Guid HoldingId { get; init; }
}

public class GetPositionTreeQueryHandler : IRequestHandler<GetPositionTreeQuery, PositionTreeDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;

    public GetPositionTreeQueryHandler(IApplicationDbContext context, ICurrentWorkspace workspace)
    {
        _context = context;
        _workspace = workspace;
    }

    /// <summary>
    /// The holding must be the workspace company or its parent; only the workspace company's positions are returned.
    /// </summary>
    public async Task<PositionTreeDto> Handle(GetPositionTreeQuery request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var company = await _context.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
        Guard.Against.NotFound(companyId, company);

        if (request.HoldingId != company.Id && request.HoldingId != company.ParentCompanyId)
            throw new ForbiddenAccessException();

        var holding = await _context.Companies
            .FirstOrDefaultAsync(c => c.Id == request.HoldingId, cancellationToken);

        var positions = await _context.Positions
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .Select(p => new { p.Id, p.Code, p.Title, p.ParentPositionId })
            .ToListAsync(cancellationToken);

        var childrenByParent = positions
            .Where(p => p.ParentPositionId.HasValue)
            .ToLookup(p => p.ParentPositionId!.Value);

        PositionTreeItemDto Build(Guid id, string code, string title, Guid? parentId, HashSet<Guid> visited)
        {
            var item = new PositionTreeItemDto
            {
                Id = id,
                Code = code,
                Title = title,
                ParentPositionId = parentId,
                Children = new List<PositionTreeItemDto>()
            };

            foreach (var child in childrenByParent[id].OrderBy(c => c.Code))
            {
                if (visited.Add(child.Id))
                    item.Children.Add(Build(child.Id, child.Code, child.Title, child.ParentPositionId, visited));
            }

            return item;
        }

        var visited = new HashSet<Guid>();
        var roots = positions
            .Where(p => p.ParentPositionId is null)
            .OrderBy(p => p.Code)
            .Where(p => visited.Add(p.Id))
            .Select(p => Build(p.Id, p.Code, p.Title, p.ParentPositionId, visited))
            .ToList();

        return new PositionTreeDto
        {
            HoldingId = request.HoldingId,
            HoldingName = holding?.Name ?? "Holding",
            Companies =
            [
                new CompanyTreeDto
                {
                    Id = company.Id,
                    Name = company.Name,
                    Positions = roots
                }
            ]
        };
    }
}
