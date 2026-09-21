using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Mappings;
using CleanArchitecture.Application.Organization.Positions.Queries;
using CleanArchitecture.Domain.Organization;
using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.Organization.Positions.Queries;

public record GetPositionQuery : IRequest<PositionDetailDto?>
{
    public Guid Id { get; init; }
}

public class GetPositionQueryHandler : IRequestHandler<GetPositionQuery, PositionDetailDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;

    public GetPositionQueryHandler(IApplicationDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<PositionDetailDto?> Handle(GetPositionQuery request, CancellationToken cancellationToken)
    {
        var position = await _context.Positions
            .Include(p => p.ParentPosition)
            .Include(p => p.Children)
            .Include(p => p.Assignments)
                .ThenInclude(pp => pp.Personnel)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (position == null)
            return null;

        return new PositionDetailDto
        {
            Id = position.Id,
            Code = position.Code,
            Title = position.Title,
            Description = position.Description,
            CompanyId = position.CompanyId,
            CompanyName = position.CompanyId.ToString(),
            ParentPositionId = position.ParentPositionId,
            ParentPositionTitle = position.ParentPosition?.Title,
            Children = position.Children.Select(c => new PositionDto
            {
                Id = c.Id,
                Code = c.Code,
                Title = c.Title,
                CompanyId = c.CompanyId,
                CompanyName = c.CompanyId.ToString(),
                ParentPositionId = c.ParentPositionId,
                ParentPositionTitle = c.ParentPosition?.Title,
                PersonnelCount = c.Assignments.Count(a => a.IsCurrentlyEffective()),
                Status = c.Status
            }).ToList(),
            Personnel = position.Assignments
                .Where(a => a.IsCurrentlyEffective())
                .Select(a => new PersonnelPositionDto
                {
                    PersonnelPositionId = a.PersonnelId,
                    PositionId = a.PositionId,
                    PositionCode = position.Code,
                    PositionTitle = position.Title,
                    IsPrimary = a.IsPrimary,
                    EffectiveFrom = a.EffectiveFrom,
                    EffectiveTo = a.EffectiveTo,
                    Status = a.Status,
                    AccessGroupHint = "Role-based access group hint" // Would come from UserCompany roles
                }).ToList(),
            Attachments = new List<AttachmentDto>(),
            Status = position.Status
        };
    }
}