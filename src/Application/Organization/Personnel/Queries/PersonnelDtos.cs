using CleanArchitecture.Application.Common.Mappings;
using CleanArchitecture.Domain.Organization;

namespace CleanArchitecture.Application.Organization.Personnel.Queries;

public record PersonnelDto
{
    public Guid Id { get; init; }
    public string PrimaryPersonnelCode { get; init; } = null!;
    public string FullName { get; init; } = null!;
    public string NationalCode { get; init; } = null!;
    public Guid CompanyId { get; init; }
    public string CompanyName { get; init; } = null!;
    public string PrimaryPositionTitle { get; init; } = null!;
    public PersonnelStatus Status { get; init; }
    public string SignatureStatus { get; init; } = null!;
}

public record PersonnelDetailDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = null!;
    public string LastName { get; init; } = null!;
    public string NationalCode { get; init; } = null!;
    public Gender Gender { get; init; }
    public string? PhoneNumber { get; init; }
    public Guid CompanyId { get; init; }
    public PersonnelStatus Status { get; init; }
    public List<PersonnelPositionDto> Positions { get; init; } = new();
    public List<AttachmentDto> Attachments { get; init; } = new();
    public SignatureDto? Signature { get; init; }
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

public record SignatureDto
{
    public Guid Id { get; init; }
    public int Version { get; init; }
    public string Url { get; init; } = null!;
    public bool IsCurrent { get; init; }
}

public record AttachmentDto
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = null!;
    public string Url { get; init; } = null!;
}