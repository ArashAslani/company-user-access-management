using CleanArchitecture.Application.AccessControl.Scopes.Queries;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.AccessControl.Scopes.Queries;

public record GetWorkshopsQuery : IRequest<List<WorkshopDto>>
{
    public Guid CompanyId { get; init; }
}

public class GetWorkshopsQueryHandler : IRequestHandler<GetWorkshopsQuery, List<WorkshopDto>>
{
    private readonly IApplicationDbContext _context;

    public GetWorkshopsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<WorkshopDto>> Handle(GetWorkshopsQuery request, CancellationToken cancellationToken)
    {
        // In a real implementation, this would query Workshop entities
        // For now, we return an empty list as Workshop entity is not yet implemented
        // This would be replaced when Workshop entity is added to the domain
        return new List<WorkshopDto>();
    }
}