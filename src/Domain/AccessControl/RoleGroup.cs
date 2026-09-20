using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.AccessControl;

public sealed class RoleGroup : BaseAuditableEntity<Guid>
{
    public override Guid Id { get; protected set; }
    public Guid CompanyId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid PrincipalId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public RoleGroupStatus Status { get; private set; }

    private readonly List<RoleGroupRole> _roleGroupRoles = [];
    public IReadOnlyCollection<RoleGroupRole> RoleGroupRoles => _roleGroupRoles.AsReadOnly();

    private readonly List<AccessRule> _accessRules = [];
    public IReadOnlyCollection<AccessRule> AccessRules => _accessRules.AsReadOnly();

    private RoleGroup() { }

    public RoleGroup(Guid companyId, Guid applicationId, string name, string? description = null)
    {
        Id = Guid.NewGuid();
        CompanyId = companyId;
        ApplicationId = applicationId;
        Name = name;
        Description = description;
        Status = RoleGroupStatus.Active;
        PrincipalId = Guid.NewGuid();
    }

    public void UpdateDetails(string name, string? description)
    {
        Name = name;
        Description = description;
    }

    public void SetStatus(RoleGroupStatus status)
    {
        Status = status;
    }

    public void AddRole(Guid roleId)
    {
        if (_roleGroupRoles.Any(r => r.RoleId == roleId))
            return;

        _roleGroupRoles.Add(new RoleGroupRole(Id, roleId));
    }

    public void RemoveRole(Guid roleId)
    {
        var rgr = _roleGroupRoles.FirstOrDefault(r => r.RoleId == roleId);
        if (rgr is not null)
            _roleGroupRoles.Remove(rgr);
    }

    public void AddAccessRule(AccessRule rule)
    {
        _accessRules.Add(rule);
    }
}

public enum RoleGroupStatus { Active, Inactive }