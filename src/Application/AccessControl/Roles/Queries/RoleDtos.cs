using CleanArchitecture.Domain.AccessControl;

namespace CleanArchitecture.Application.AccessControl.Roles.Queries;

public record RoleDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = null!;
    public string Title { get; init; } = null!;
    public int UserCount { get; init; }
    public RoleStatus Status { get; init; }
}

public record RoleDetailDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = null!;
    public string Title { get; init; } = null!;
    public string? Description { get; init; }
    public RoleKind Kind { get; init; }
    public RoleStatus Status { get; init; }
    public DateTime? ValidUntil { get; init; }
    public Guid? ParentRoleId { get; init; }
    public string? ParentRoleTitle { get; init; }
    public List<RoleDto> Children { get; init; } = new();
    public List<UserDto> Users { get; init; } = new();
    public List<PermissionDto> Permissions { get; init; } = new();
}

public record UserDto
{
    public Guid UserId { get; init; }
    public string FullName { get; init; } = null!;
}

public record PermissionDto
{
    public Guid ResourceId { get; init; }
    public string ResourceName { get; init; } = null!;
    public string ActionCode { get; init; } = null!;
    public AccessEffect Effect { get; init; }
    public string ScopeMode { get; init; } = null!;
    public List<ScopeDto> Scopes { get; init; } = new();
}

public record ScopeDto
{
    public string ScopeType { get; init; } = null!;
    public string ScopeKey { get; init; } = null!;
}

public record RoleTreeDto
{
    public Guid HoldingId { get; init; }
    public string HoldingName { get; init; } = null!;
    public List<CompanyRoleTreeDto> Companies { get; init; } = new();
}

public record CompanyRoleTreeDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public List<RoleTreeItemDto> Roles { get; init; } = new();
}

public record RoleTreeItemDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = null!;
    public string Title { get; init; } = null!;
    public Guid? ParentRoleId { get; init; }
    public List<RoleTreeItemDto> Children { get; init; } = new();
}