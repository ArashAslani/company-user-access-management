using CleanArchitecture.Application.Common.Mappings;
using CleanArchitecture.Domain.Organization;
using AutoMapper;

namespace CleanArchitecture.Application.Organization.Positions.Queries;

public record PositionDto : IMapFrom<Position>
{
    public Guid Id { get; init; }
    public string Code { get; init; } = null!;
    public string Title { get; init; } = null!;
    public Guid CompanyId { get; init; }
    public string CompanyName { get; init; } = null!;
    public Guid? ParentPositionId { get; init; }
    public string? ParentPositionTitle { get; init; }
    public int PersonnelCount { get; init; }
    public PositionStatus Status { get; init; }

    public void Mapping(Profile profile)
    {
        profile.CreateMap<Position, PositionDto>()
            .ForMember(d => d.CompanyName, opt => opt.MapFrom(s => s.CompanyId.ToString()));
    }
}

public record PositionDetailDto : IMapFrom<Position>
{
    public Guid Id { get; init; }
    public string Code { get; init; } = null!;
    public string Title { get; init; } = null!;
    public string? Description { get; init; }
    public Guid CompanyId { get; init; }
    public string CompanyName { get; init; } = null!;
    public Guid? ParentPositionId { get; init; }
    public string? ParentPositionTitle { get; init; }
    public List<PositionDto> Children { get; init; } = new();
    public List<PersonnelPositionDto> Personnel { get; init; } = new();
    public List<AttachmentDto> Attachments { get; init; } = new();
    public PositionStatus Status { get; init; }

    public void Mapping(Profile profile)
    {
        profile.CreateMap<Position, PositionDetailDto>();
    }
}

public record PositionTreeDto
{
    public Guid HoldingId { get; init; }
    public string HoldingName { get; init; } = null!;
    public List<CompanyTreeDto> Companies { get; init; } = new();
}

public record CompanyTreeDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public List<PositionTreeItemDto> Positions { get; init; } = new();
}

public record PositionTreeItemDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = null!;
    public string Title { get; init; } = null!;
    public Guid? ParentPositionId { get; init; }
    public List<PositionTreeItemDto> Children { get; init; } = new();
}

public record PositionSummaryDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = null!;
    public string Title { get; init; } = null!;
    public PositionStatus Status { get; init; }
    public string? Description { get; init; }
    public List<PersonnelSummaryDto> Personnel { get; init; } = new();
}

public record PersonnelSummaryDto
{
    public Guid PersonnelId { get; init; }
    public string FullName { get; init; } = null!;
}

public record PersonnelPositionDto
{
    public Guid PersonnelPositionId { get; init; }
    public Guid PositionId { get; init; }
    public string PositionCode { get; init; } = null!;
    public string PositionTitle { get; init; } = null!;
    public bool IsPrimary { get; init; }
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public PersonnelPositionStatus Status { get; init; }
    public string AccessGroupHint { get; init; } = null!;
}

public record AttachmentDto
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = null!;
    public string Url { get; init; } = null!;
}