using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;

namespace CompanyAccessManagement.Application.AccessControl.Roles.Queries;

public record GetRoleTreeQuery : IRequest<RoleTreeDto>
{
    public Guid HoldingId { get; init; }
}

public class GetRoleTreeQueryHandler : IRequestHandler<GetRoleTreeQuery, RoleTreeDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;

    public GetRoleTreeQueryHandler(IApplicationDbContext context, ICurrentWorkspace workspace)
    {
        _context = context;
        _workspace = workspace;
    }

    /// <summary>
    /// The holding must be the workspace company or its parent; only the workspace company's roles are returned.
    /// </summary>
    public async Task<RoleTreeDto> Handle(GetRoleTreeQuery request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var company = await _context.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
        Guard.Against.NotFound(companyId, company);

        if (request.HoldingId != company.Id && request.HoldingId != company.ParentCompanyId)
            throw new ForbiddenAccessException();

        var holding = await _context.Companies
            .FirstOrDefaultAsync(c => c.Id == request.HoldingId, cancellationToken);

        var roles = await _context.Roles
            .AsNoTracking()
            .InAccessControlApplication(_context)
            .Where(r => r.CompanyId == companyId)
            .Select(r => new { r.Id, r.Code, r.Name, r.ParentRoleId })
            .ToListAsync(cancellationToken);

        var childrenByParent = roles
            .Where(r => r.ParentRoleId.HasValue)
            .ToLookup(r => r.ParentRoleId!.Value);

        RoleTreeItemDto Build(Guid id, string code, string name, Guid? parentId, HashSet<Guid> visited)
        {
            var item = new RoleTreeItemDto
            {
                Id = id,
                Code = code,
                Title = name,
                ParentRoleId = parentId,
                Children = new List<RoleTreeItemDto>()
            };

            foreach (var child in childrenByParent[id].OrderBy(c => c.Code))
            {
                if (visited.Add(child.Id))
                    item.Children.Add(Build(child.Id, child.Code, child.Name, child.ParentRoleId, visited));
            }

            return item;
        }

        var visited = new HashSet<Guid>();
        var roots = roles
            .Where(r => r.ParentRoleId is null)
            .OrderBy(r => r.Code)
            .Where(r => visited.Add(r.Id))
            .Select(r => Build(r.Id, r.Code, r.Name, r.ParentRoleId, visited))
            .ToList();

        return new RoleTreeDto
        {
            HoldingId = request.HoldingId,
            HoldingName = holding?.Name ?? "Holding",
            Companies =
            [
                new CompanyRoleTreeDto
                {
                    Id = company.Id,
                    Name = company.Name,
                    Roles = roots
                }
            ]
        };
    }
}
