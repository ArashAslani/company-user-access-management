using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Models;
using CompanyAccessManagement.Application.Organization.Personnel.Queries;
using CompanyAccessManagement.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Personnel.Queries;

public record GetPersonnelQuery : IRequest<PaginatedList<PersonnelDto>>
{
    public Guid? CompanyId { get; init; }
    public string? Status { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Sort { get; init; }
}

public class GetPersonnelQueryHandler : IRequestHandler<GetPersonnelQuery, PaginatedList<PersonnelDto>>
{
    private readonly IApplicationDbContext _context;

    public GetPersonnelQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<PersonnelDto>> Handle(GetPersonnelQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Personnel
            .Include(p => p.Positions)
                .ThenInclude(pp => pp.Position)
            .AsQueryable();

        if (request.CompanyId.HasValue)
            query = query.Where(p => p.Positions.Any(pp => pp.Position != null && pp.Position.CompanyId == request.CompanyId.Value));

        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<PersonnelStatus>(request.Status, true, out var status))
            query = query.Where(p => p.Status == status);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            query = query.Where(p => p.NationalCode.Contains(request.Search) 
                || p.FirstName.Contains(request.Search) 
                || p.LastName.Contains(request.Search));
        }

        if (!string.IsNullOrWhiteSpace(request.Sort))
        {
            var parts = request.Sort.Split(':');
            var field = parts[0];
            var dir = parts.Length > 1 ? parts[1] : "asc";

            query = field.ToLower() switch
            {
                "nationalcode" => dir == "desc" ? query.OrderByDescending(p => p.NationalCode) : query.OrderBy(p => p.NationalCode),
                "fullname" => dir == "desc" ? query.OrderByDescending(p => p.FirstName + " " + p.LastName) : query.OrderBy(p => p.FirstName + " " + p.LastName),
                _ => query.OrderBy(p => p.NationalCode)
            };
        }
        else
        {
            query = query.OrderBy(p => p.NationalCode);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var personnelList = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = personnelList.Select(p =>
        {
            var effectivePos = p.Positions.FirstOrDefault(pp => pp.Position != null && pp.IsCurrentlyEffective());
            var primaryPos = p.Positions.FirstOrDefault(pp => pp.IsPrimary && pp.IsCurrentlyEffective());

            return new PersonnelDto
            {
                Id = p.Id,
                PrimaryPersonnelCode = p.PersonnelCode ?? string.Empty,
                FullName = p.FirstName + " " + p.LastName,
                NationalCode = p.NationalCode,
                CompanyId = effectivePos?.Position?.CompanyId ?? Guid.Empty,
                CompanyName = effectivePos?.Position?.CompanyId.ToString() ?? string.Empty,
                PrimaryPositionTitle = primaryPos?.Position?.Title ?? string.Empty,
                Status = p.Status,
                SignatureStatus = p.GetCurrentSignature() != null ? "Registered" : "NotRegistered"
            };
        }).ToList();

        return new PaginatedList<PersonnelDto>(items, totalCount, request.Page, request.PageSize);
    }
}
