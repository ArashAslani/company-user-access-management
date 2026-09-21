using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.AccessControl.Roles.Queries;
using CleanArchitecture.Domain.AccessControl;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.AccessControl.Roles.Queries;

public record GetRoleTreeQuery : IRequest<RoleTreeDto>
{
    public Guid HoldingId { get; init; }
}

public class GetRoleTreeQueryHandler : IRequestHandler<GetRoleTreeQuery, RoleTreeDto>
{
    private readonly IApplicationDbContext _context;

    public GetRoleTreeQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RoleTreeDto> Handle(GetRoleTreeQuery request, CancellationToken cancellationToken)
    {
        var companies = await _context.Companies
            .Where(c => c.ParentCompanyId == request.HoldingId || c.Id == request.HoldingId)
            .ToListAsync(cancellationToken);

        var holding = await _context.Companies
            .FirstOrDefaultAsync(c => c.Id == request.HoldingId, cancellationToken);

        var tree = new RoleTreeDto
        {
            HoldingId = request.HoldingId,
            HoldingName = holding?.Name ?? "Holding",
            Companies = new List<CompanyRoleTreeDto>()
        };

        foreach (var company in companies)
        {
            var rootRoles = await _context.Roles
                .Where(r => r.CompanyId == company.Id && r.ParentRoleId == null)
                .ToListAsync(cancellationToken);

            var companyTree = new CompanyRoleTreeDto
            {
                Id = company.Id,
                Name = company.Name,
                Roles = new List<RoleTreeItemDto>()
            };

            foreach (var root in rootRoles)
            {
                companyTree.Roles.Add(await BuildRoleTreeItem(root, cancellationToken));
            }

            tree.Companies.Add(companyTree);
        }

        return tree;
    }

    private async Task<RoleTreeItemDto> BuildRoleTreeItem(Role role, CancellationToken cancellationToken)
    {
        var children = await _context.Roles
            .Where(r => r.ParentRoleId == role.Id)
            .ToListAsync(cancellationToken);

        var item = new RoleTreeItemDto
        {
            Id = role.Id,
            Code = role.Code,
            Title = role.Name,
            ParentRoleId = role.ParentRoleId,
            Children = new List<RoleTreeItemDto>()
        };

        foreach (var child in children)
        {
            item.Children.Add(await BuildRoleTreeItem(child, cancellationToken));
        }

        return item;
    }
}