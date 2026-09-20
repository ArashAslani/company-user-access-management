using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.AccessControl;

public sealed class RoleGroupRole : BaseEntity
{
    public Guid RoleGroupId { get; private set; }
    public Guid RoleId { get; private set; }

    public RoleGroup? RoleGroup { get; private set; }
    public Role? Role { get; private set; }

    private RoleGroupRole() { }

    public RoleGroupRole(Guid roleGroupId, Guid roleId)
    {
        RoleGroupId = roleGroupId;
        RoleId = roleId;
    }
}