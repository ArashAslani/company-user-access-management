using CompanyAccessManagement.Domain.Common;

namespace CompanyAccessManagement.Domain.AccessControl;

public sealed class UserRole : BaseEntity
{
    public Guid UserCompanyId { get; private set; }
    public Guid RoleId { get; private set; }

    public UserCompany? UserCompany { get; private set; }
    public Role? Role { get; private set; }

    private UserRole() { }

    public UserRole(Guid userCompanyId, Guid roleId)
    {
        UserCompanyId = userCompanyId;
        RoleId = roleId;
    }
}
