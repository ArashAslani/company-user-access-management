using CompanyAccessManagement.Domain.Common;

namespace CompanyAccessManagement.Domain.AccessControl;

public sealed class Permission : BaseAuditableEntity<Guid>
{
    public override Guid Id { get; protected set; }
    public Guid ResourceId { get; private set; }
    public string ActionCode { get; private set; } = null!;
    public string? Description { get; private set; }
    public string FullCode => $"{Resource?.Code}.{ActionCode}";

    public Resource? Resource { get; private set; }

    private readonly List<AccessRule> _accessRules = [];
    public IReadOnlyCollection<AccessRule> AccessRules => _accessRules.AsReadOnly();

    private readonly List<PermissionImplication> _implications = [];
    public IReadOnlyCollection<PermissionImplication> Implications => _implications.AsReadOnly();

    private Permission() { }

    public Permission(Guid resourceId, string actionCode, string? description = null)
    {
        Id = Guid.NewGuid();
        ResourceId = resourceId;
        ActionCode = actionCode;
        Description = description;
    }

    public void UpdateDetails(string actionCode, string? description)
    {
        ActionCode = actionCode;
        Description = description;
    }

    public void AddImplication(Guid requiredPermissionId)
    {
        if (_implications.Any(i => i.RequiredPermissionId == requiredPermissionId))
            return;

        _implications.Add(new PermissionImplication(Id, requiredPermissionId));
    }

    public void RemoveImplication(Guid requiredPermissionId)
    {
        var imp = _implications.FirstOrDefault(i => i.RequiredPermissionId == requiredPermissionId);
        if (imp is not null)
            _implications.Remove(imp);
    }
}

public sealed class PermissionImplication : BaseEntity
{
    public Guid PermissionId { get; private set; }
    public Guid RequiredPermissionId { get; private set; }

    public Permission? Permission { get; private set; }
    public Permission? RequiredPermission { get; private set; }

    private PermissionImplication() { }

    public PermissionImplication(Guid permissionId, Guid requiredPermissionId)
    {
        PermissionId = permissionId;
        RequiredPermissionId = requiredPermissionId;
    }
}
