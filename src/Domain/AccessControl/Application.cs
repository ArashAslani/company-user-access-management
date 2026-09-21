using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.AccessControl;

public sealed class Application : BaseAuditableEntity<Guid>
{
    public override Guid Id { get; protected set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public long PolicyRevision { get; private set; }

    private readonly List<Resource> _resources = [];
    public IReadOnlyCollection<Resource> Resources => _resources.AsReadOnly();

    private readonly List<Role> _roles = [];
    public IReadOnlyCollection<Role> Roles => _roles.AsReadOnly();

    private Application() { }

    public Application(string code, string name, string? description = null)
    {
        Id = Guid.NewGuid();
        Code = code;
        Name = name;
        Description = description;
        IsActive = true;
        PolicyRevision = 1;
    }

    public void UpdateDetails(string name, string? description, bool isActive)
    {
        Name = name;
        Description = description;
        IsActive = isActive;
    }

    public void IncrementPolicyRevision() => PolicyRevision++;

    public Resource AddResource(string code, string name, string? description = null, Guid? parentResourceId = null)
    {
        if (_resources.Any(r => r.Code == code))
            throw new InvalidOperationException($"Resource with code {code} already exists.");

        var resource = new Resource(Id, code, name, description, parentResourceId);
        _resources.Add(resource);
        return resource;
    }
}