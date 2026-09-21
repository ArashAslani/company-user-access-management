using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.AccessControl;

public sealed class AuthPrincipal : BaseAuditableEntity<Guid>
{
    public override Guid Id { get; protected set; }
    public PrincipalType Type { get; private set; }
    public Guid ReferenceId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ApplicationId { get; private set; }

    private readonly List<AccessRule> _accessRules = [];
    public IReadOnlyCollection<AccessRule> AccessRules => _accessRules.AsReadOnly();

    private AuthPrincipal() { }

    public AuthPrincipal(PrincipalType type, Guid referenceId, Guid companyId, Guid applicationId)
    {
        Id = Guid.NewGuid();
        Type = type;
        ReferenceId = referenceId;
        CompanyId = companyId;
        ApplicationId = applicationId;
    }

    public static AuthPrincipal ForUserCompany(Guid userCompanyId, Guid companyId, Guid applicationId)
        => new(PrincipalType.UserCompany, userCompanyId, companyId, applicationId);

    public static AuthPrincipal ForRole(Guid roleId, Guid companyId, Guid applicationId)
        => new(PrincipalType.Role, roleId, companyId, applicationId);

    public void AddAccessRule(AccessRule rule)
    {
        _accessRules.Add(rule);
    }
}

public enum PrincipalType { UserCompany, Role }