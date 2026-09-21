using CleanArchitecture.Application.AccessControl.Scopes.Queries;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.AccessControl;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.AccessControl.Scopes.Queries;

public record GetResourceTreeQuery : IRequest<List<ScopeResourceTreeDto>>
{
    public Guid ApplicationId { get; init; }
}

public class GetResourceTreeQueryHandler : IRequestHandler<GetResourceTreeQuery, List<ScopeResourceTreeDto>>
{
    private readonly IApplicationDbContext _context;

    public GetResourceTreeQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<ScopeResourceTreeDto>> Handle(GetResourceTreeQuery request, CancellationToken cancellationToken)
    {
        var rootResources = await _context.Resources
            .Where(r => r.ApplicationId == request.ApplicationId && r.ParentResourceId == null)
            .ToListAsync(cancellationToken);

        var result = new List<ScopeResourceTreeDto>();

        foreach (var root in rootResources)
        {
            result.Add(await BuildResourceTree(root, cancellationToken));
        }

        return result;
    }

    private async Task<ScopeResourceTreeDto> BuildResourceTree(Resource resource, CancellationToken cancellationToken)
    {
        var children = await _context.Resources
            .Where(r => r.ParentResourceId == resource.Id)
            .ToListAsync(cancellationToken);

        var permissions = await _context.Permissions
            .Where(p => p.ResourceId == resource.Id)
            .ToListAsync(cancellationToken);

        var dto = new ScopeResourceTreeDto
        {
            Id = resource.Id,
            Code = resource.Code,
            Name = resource.Name,
            Children = new List<ScopeResourceTreeDto>(),
            Actions = permissions.Select(p => new ResourceActionDto
            {
                ActionCode = p.ActionCode,
                Name = p.ActionCode
            }).ToList()
        };

        foreach (var child in children)
        {
            dto.Children.Add(await BuildResourceTree(child, cancellationToken));
        }

        return dto;
    }
}