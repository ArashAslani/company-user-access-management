using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.AccessControl.Roles.Queries;
using CompanyAccessManagement.Application.Common.Models;
using CompanyAccessManagement.Domain.AccessControl;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.AccessControl.Roles.Queries;

public record GetRolesQuery : IRequest<PaginatedList<RoleDto>>
{
    public Guid? CompanyId { get; init; }
    public Guid? ApplicationId { get; init; }
    public string? Status { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Sort { get; init; }
}

public class GetRolesQueryHandler : IRequestHandler<GetRolesQuery, PaginatedList<RoleDto>>
{
    private readonly IApplicationDbContext _context;

    public GetRolesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<RoleDto>> Handle(GetRolesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Roles
            .Include(r => r.UserRoles)
            .AsQueryable();

        if (request.CompanyId.HasValue)
            query = query.Where(r => r.CompanyId == request.CompanyId.Value);

        if (request.ApplicationId.HasValue)
            query = query.Where(r => r.ApplicationId == request.ApplicationId.Value);

        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<RoleStatus>(request.Status, true, out var status))
            query = query.Where(r => r.Status == status);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            query = query.Where(r => r.Code.Contains(request.Search) || r.Name.Contains(request.Search));
        }

        if (!string.IsNullOrWhiteSpace(request.Sort))
        {
            var parts = request.Sort.Split(':');
            var field = parts[0];
            var dir = parts.Length > 1 ? parts[1] : "asc";

            query = field.ToLower() switch
            {
                "code" => dir == "desc" ? query.OrderByDescending(r => r.Code) : query.OrderBy(r => r.Code),
                "title" => dir == "desc" ? query.OrderByDescending(r => r.Name) : query.OrderBy(r => r.Name),
                _ => query.OrderBy(r => r.Code)
            };
        }
        else
        {
            query = query.OrderBy(r => r.Code);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var roles = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = roles.Select(r => new RoleDto
        {
            Id = r.Id,
            Code = r.Code,
            Title = r.Name,
            UserCount = r.UserRoles.Count,
            Status = r.Status
        }).ToList();

        return new PaginatedList<RoleDto>(items, totalCount, request.Page, request.PageSize);
    }
}
