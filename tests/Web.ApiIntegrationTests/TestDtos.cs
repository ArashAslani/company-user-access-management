using CompanyAccessManagement.Domain.Organization;
using CompanyAccessManagement.Domain.AccessControl;

namespace CompanyAccessManagement.ApiIntegrationTests;

// Shared DTOs for API integration tests
public record PaginatedList<T>(List<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

// Position DTOs
public record PositionDto(Guid Id, string Code, string Title, string? Description, Guid? ParentPositionId, PositionStatus Status);

public record PositionDetailDto(
    Guid Id,
    string Code,
    string Title,
    string? Description,
    Guid CompanyId,
    Guid? ParentPositionId,
    PositionStatus Status,
    DateTimeOffset Created,
    DateTimeOffset? LastModified);

public record PositionSummaryDto(
    Guid Id,
    string Code,
    string Title,
    int ActiveAssignmentsCount,
    int TotalAssignmentsCount,
    List<PositionSummaryDto> Children);

public record PositionTreeDto(
    Guid Id,
    string Code,
    string Title,
    List<PositionTreeDto> Children);

// Personnel DTOs
public record PersonnelDto(Guid Id, string NationalCode, string FirstName, string LastName, Gender Gender, string PersonnelCode, string? PhoneNumber, PersonnelStatus Status);

public record PersonnelDetailDto(
    Guid Id,
    string NationalCode,
    string FirstName,
    string LastName,
    Gender Gender,
    string PersonnelCode,
    string? PhoneNumber,
    PersonnelStatus Status,
    Guid CompanyId,
    string CompanyName,
    List<PersonnelPositionDto> Assignments,
    DateTimeOffset Created,
    DateTimeOffset? LastModified);

public record PersonnelPositionDto(
    Guid Id,
    Guid PositionId,
    string PositionCode,
    string PositionTitle,
    bool IsPrimary,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    PersonnelPositionStatus Status);

// Role DTOs
public record RoleDto(Guid Id, string Code, string Name, RoleKind Kind, Guid? ParentRoleId, RoleStatus Status);

public record RoleDetailDto(
    Guid Id,
    string Code,
    string Name,
    RoleKind Kind,
    Guid CompanyId,
    Guid ApplicationId,
    Guid? ParentRoleId,
    RoleStatus Status,
    List<RolePermissionDto> Permissions,
    DateTimeOffset Created,
    DateTimeOffset? LastModified);

public record RolePermissionDto(Guid PermissionId, string ResourceCode, string ActionCode);

public record RoleTreeDto(
    Guid Id,
    string Code,
    string Name,
    RoleKind Kind,
    List<RoleTreeDto> Children);

// Audit DTOs
public record AuditLogDto(
    Guid OperationId,
    Guid ActorUserId,
    string ActorUserName,
    Guid CompanyId,
    string CompanyName,
    string ChangeType,
    Guid ResourceId,
    string Source,
    DateTimeOffset Timestamp);

public record AuditLogDetailDto(
    Guid OperationId,
    Guid ActorUserId,
    string ActorUserName,
    Guid CompanyId,
    string CompanyName,
    string ChangeType,
    Guid ResourceId,
    string Source,
    DateTimeOffset Timestamp,
    Dictionary<string, object?> Changes);

// Attachment DTOs
public record AttachmentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long Size,
    Guid EntityType,
    Guid EntityId,
    string? Description,
    DateTimeOffset UploadedAt,
    Guid UploadedByUserId);

// Scope DTOs
public record WorkshopDto(Guid Id, string Code, string Name);
public record ScopeResourceTreeDto(Guid Id, string Code, string Name, string ResourceType, List<ScopeResourceTreeDto> Children);

// Response records
public record CreatePositionResponse(Guid Id);
public record CreatePersonnelResponse(Guid Id);
public record CreateRoleResponse(Guid Id);