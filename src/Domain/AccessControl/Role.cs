using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.AccessControl;

public sealed class Role : BaseAuditableEntity<Guid>
{
    public override Guid Id { get; protected set; }
    public Guid CompanyId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid? ParentRoleId { get; private set; }
    public Guid PrincipalId { get; private set; }
    public string Name { get; private set; } = null!;
    public RoleKind Kind { get; private set; }
    public DateTime? ValidUntil { get; private set; }
    public RoleStatus Status { get; private set; }

    public Role? ParentRole { get; private set; }
    private readonly List<Role> _children = [];
    public IReadOnlyCollection<Role> Children => _children.AsReadOnly();

    private readonly List<UserRole> _userRoles = [];
    public IReadOnlyCollection<UserRole> UserRoles => _userRoles.AsReadOnly();

    private readonly List<RoleGroupRole> _roleGroupRoles = [];
    public IReadOnlyCollection<RoleGroupRole> RoleGroupRoles => _roleGroupRoles.AsReadOnly();

    private readonly List<AccessRule> _accessRules = [];
    public IReadOnlyCollection<AccessRule> AccessRules => _accessRules.AsReadOnly();

    private Role() { }

    public Role(Guid companyId, Guid applicationId, string name, RoleKind kind = RoleKind.Standard, Guid? parentRoleId = null, DateTime? validUntil = null)
    {
        Id = Guid.NewGuid();
        CompanyId = companyId;
        ApplicationId = applicationId;
        Name = name;
        Kind = kind;
        ParentRoleId = parentRoleId;
        ValidUntil = validUntil;
        Status = RoleStatus.Active;
        PrincipalId = Guid.NewGuid();
    }

    public void UpdateDetails(string name, RoleKind kind, DateTime? validUntil)
    {
        Name = name;
        Kind = kind;
        ValidUntil = validUntil;
    }

    public void ChangeParent(Guid? newParentRoleId)
    {
        if (newParentRoleId == Id)
            throw new InvalidOperationException("Role cannot be its own parent.");

        ParentRoleId = newParentRoleId;
    }

    public void SetStatus(RoleStatus status)
    {
        Status = status;
    }

    public void AddAccessRule(AccessRule rule)
    {
        _accessRules.Add(rule);
    }

    public void RemoveAccessRule(Guid ruleId)
    {
        var rule = _accessRules.FirstOrDefault(r => r.Id == ruleId);
        if (rule is not null)
            _accessRules.Remove(rule);
    }
}

public enum RoleKind { Standard, CompanySuperAdmin, GlobalSuperAdmin }

public enum RoleStatus { Active, Inactive }