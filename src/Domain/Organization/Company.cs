using CompanyAccessManagement.Domain.Common;

namespace CompanyAccessManagement.Domain.Organization;

public sealed class Company : BaseAuditableEntity<Guid>
{
    public override Guid Id { get; protected set; }
    public Guid? ParentCompanyId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public CompanyStatus Status { get; private set; }
    public string? ExternalSource { get; private set; }
    public string? ExternalId { get; private set; }
    /// <summary>Optimistic concurrency token bumped by organization topology changes (position hierarchy).</summary>
    public long OrganizationRevision { get; private set; } = 1;
    /// <summary>Optimistic concurrency token and cache key part bumped by authorization topology changes (roles, role rules).</summary>
    public long AuthorizationRevision { get; private set; } = 1;

    public Company? ParentCompany { get; private set; }
    private readonly List<Company> _children = [];
    public IReadOnlyCollection<Company> Children => _children.AsReadOnly();

    private Company() { }

    public Company(string code, string name, string? description = null, Guid? parentCompanyId = null)
    {
        Id = Guid.NewGuid();
        Code = code;
        Name = name;
        Description = description;
        ParentCompanyId = parentCompanyId;
        Status = CompanyStatus.Active;
    }

    public void UpdateDetails(string code, string name, string? description, CompanyStatus status)
    {
        Code = code;
        Name = name;
        Description = description;
        Status = status;
    }

    public void ChangeParent(Guid? newParentCompanyId)
    {
        if (newParentCompanyId == Id)
            throw new DomainRuleViolationException("HIERARCHY_CYCLE", "Company cannot be its own parent.");

        ParentCompanyId = newParentCompanyId;
    }

    public void TouchOrganization() => OrganizationRevision++;

    public void TouchAuthorization() => AuthorizationRevision++;

    public void SetExternalIdentity(string? source, string? id) => (ExternalSource, ExternalId) = ExternalIdentity.Normalize(source, id);
}

public enum CompanyStatus { Active, Inactive }
