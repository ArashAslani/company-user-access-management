using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.Organization;

public sealed class Company : BaseAuditableEntity<Guid>
{
    public override Guid Id { get; protected set; }
    public Guid? ParentCompanyId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public CompanyStatus Status { get; private set; }

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
            throw new InvalidOperationException("Company cannot be its own parent.");

        ParentCompanyId = newParentCompanyId;
    }
}

public enum CompanyStatus { Active, Inactive }