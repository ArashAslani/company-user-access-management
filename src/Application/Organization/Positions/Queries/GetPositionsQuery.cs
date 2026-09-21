using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Domain.Organization;
using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.Organization.Positions.Queries;

public record GetPositionsQuery : IRequest<PaginatedList<PositionDto>>
{
    public Guid? CompanyId { get; init; }
    public string? Status { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Sort { get; init; }
}

public class GetPositionsQueryHandler : IRequestHandler<GetPositionsQuery, PaginatedList<PositionDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;

    public GetPositionsQueryHandler(IApplicationDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<PaginatedList<PositionDto>> Handle(GetPositionsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Positions
            .Include(p => p.ParentPosition)
            .Include(p => p.Assignments)
            .AsQueryable();

        if (request.CompanyId.HasValue)
            query = query.Where(p => p.CompanyId == request.CompanyId.Value);

        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<PositionStatus>(request.Status, true, out var status))
            query = query.Where(p => p.Status == status);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            query = query.Where(p => p.Code.Contains(request.Search) || p.Title.Contains(request.Search));
        }

        if (!string.IsNullOrWhiteSpace(request.Sort))
        {
            var parts = request.Sort.Split(':');
            var field = parts[0];
            var dir = parts.Length > 1 ? parts[1] : "asc";

            query = field.ToLower() switch
            {
                "code" => dir == "desc" ? query.OrderByDescending(p => p.Code) : query.OrderBy(p => p.Code),
                "title" => dir == "desc" ? query.OrderByDescending(p => p.Title) : query.OrderBy(p => p.Title),
                _ => query.OrderBy(p => p.Code)
            };
        }
        else
        {
            query = query.OrderBy(p => p.Code);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => new PositionDto
            {
                Id = p.Id,
                Code = p.Code,
                Title = p.Title,
                CompanyId = p.CompanyId,
                CompanyName = p.CompanyId.ToString(),
                ParentPositionId = p.ParentPositionId,
                ParentPositionTitle = p.ParentPosition != null ? p.ParentPosition.Title : null,
                PersonnelCount = p.Assignments.Count(a => a.Status == PersonnelPositionStatus.Active && a.EffectiveFrom <= DateTime.UtcNow && (a.EffectiveTo == null || a.EffectiveTo > DateTime.UtcNow)),
                Status = p.Status
            })
            .ToListAsync(cancellationToken);

        return new PaginatedList<PositionDto>(items, totalCount, request.Page, request.PageSize);
    }
}