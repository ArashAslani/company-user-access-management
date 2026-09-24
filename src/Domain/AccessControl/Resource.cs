using CompanyAccessManagement.Domain.Common;

namespace CompanyAccessManagement.Domain.AccessControl;

public sealed class Resource : BaseAuditableEntity<Guid>
{
    public override Guid Id { get; protected set; }
    public Guid ApplicationId { get; private set; }
    public Guid? ParentResourceId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public int SortOrder { get; private set; }

    public Application? Application { get; private set; }
    public Resource? ParentResource { get; private set; }
    private readonly List<Resource> _children = [];
    public IReadOnlyCollection<Resource> Children => _children.AsReadOnly();

    private readonly List<Permission> _permissions = [];
    public IReadOnlyCollection<Permission> Permissions => _permissions.AsReadOnly();

    private Resource() { }

    public Resource(Guid applicationId, string code, string name, string? description = null, Guid? parentResourceId = null, int sortOrder = 0)
    {
        Id = Guid.NewGuid();
        ApplicationId = applicationId;
        Code = code;
        Name = name;
        Description = description;
        ParentResourceId = parentResourceId;
        SortOrder = sortOrder;
    }

    public void UpdateDetails(string code, string name, string? description, int sortOrder)
    {
        Code = code;
        Name = name;
        Description = description;
        SortOrder = sortOrder;
    }

    public Permission AddPermission(string actionCode, string description = "")
    {
        if (_permissions.Any(p => p.ActionCode == actionCode))
            throw new InvalidOperationException($"Permission with action {actionCode} already exists for this resource.");

        var permission = new Permission(Id, actionCode, description);
        _permissions.Add(permission);
        return permission;
    }
}
