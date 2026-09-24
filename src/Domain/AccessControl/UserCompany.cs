using CompanyAccessManagement.Domain.Common;

namespace CompanyAccessManagement.Domain.AccessControl;

public sealed class UserCompany : BaseAuditableEntity<Guid>
{
    public override Guid Id { get; protected set; }
    public Guid UserId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PrincipalId { get; private set; }
    public UserCompanyStatus Status { get; private set; }
    public long AuthorizationRevision { get; private set; }

    private readonly List<UserRole> _roles = [];
    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    private UserCompany() { }

    public UserCompany(Guid userId, Guid companyId, Guid principalId)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        CompanyId = companyId;
        PrincipalId = principalId;
        Status = UserCompanyStatus.Active;
        AuthorizationRevision = 1;
    }

    public void SetStatus(UserCompanyStatus status)
    {
        Status = status;
        IncrementRevision();
    }

    public void AddRole(Guid roleId)
    {
        if (_roles.Any(r => r.RoleId == roleId))
            return;

        _roles.Add(new UserRole(Id, roleId));
        IncrementRevision();
    }

    public void RemoveRole(Guid roleId)
    {
        var role = _roles.FirstOrDefault(r => r.RoleId == roleId);
        if (role is not null)
        {
            _roles.Remove(role);
            IncrementRevision();
        }
    }

    public void IncrementRevision() => AuthorizationRevision++;
}

public enum UserCompanyStatus { Active, Inactive, Pending }
