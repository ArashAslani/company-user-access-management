using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.AccessControl;

public sealed class AccessRule : BaseAuditableEntity<Guid>
{
    public override Guid Id { get; protected set; }
    public Guid PrincipalId { get; private set; }
    public Guid PermissionId { get; private set; }
    public Guid? AuthorityRoleId { get; private set; }
    public Guid? DelegatedFromUserId { get; private set; }
    public AccessEffect Effect { get; private set; }
    public AccessRuleOrigin Origin { get; private set; }
    public ScopeMode ScopeMode { get; private set; }
    public DateTime? ValidFrom { get; private set; }
    public DateTime? ValidUntil { get; private set; }
    public AccessRuleStatus Status { get; private set; }

    public AuthPrincipal? Principal { get; private set; }
    public Permission? Permission { get; private set; }
    public Role? AuthorityRole { get; private set; }

    private readonly List<RuleScope> _scopes = [];
    public IReadOnlyCollection<RuleScope> Scopes => _scopes.AsReadOnly();

    private AccessRule() { }

    public AccessRule(Guid principalId, Guid permissionId, AccessEffect effect, AccessRuleOrigin origin = AccessRuleOrigin.Manual, ScopeMode scopeMode = ScopeMode.None, DateTime? validFrom = null, DateTime? validUntil = null, Guid? authorityRoleId = null, Guid? delegatedFromUserId = null)
    {
        Id = Guid.NewGuid();
        PrincipalId = principalId;
        PermissionId = permissionId;
        Effect = effect;
        Origin = origin;
        ScopeMode = scopeMode;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        AuthorityRoleId = authorityRoleId;
        DelegatedFromUserId = delegatedFromUserId;
        Status = AccessRuleStatus.Active;
    }

    public void AddScope(string scopeType, string scopeKey)
    {
        if (_scopes.Any(s => s.ScopeType == scopeType && s.ScopeKey == scopeKey))
            return;

        _scopes.Add(new RuleScope(Id, scopeType, scopeKey));
    }

    public void RemoveScope(string scopeType, string scopeKey)
    {
        var scope = _scopes.FirstOrDefault(s => s.ScopeType == scopeType && s.ScopeKey == scopeKey);
        if (scope is not null)
            _scopes.Remove(scope);
    }

    public void ClearScopes() => _scopes.Clear();

    public void SetScopeMode(ScopeMode mode)
    {
        ScopeMode = mode;
        if (mode == ScopeMode.None)
            ClearScopes();
    }

    public void SetStatus(AccessRuleStatus status) => Status = status;
}

public enum AccessEffect { Allow, Deny }

public enum AccessRuleOrigin { Manual, Delegated, System, Copy }

public enum ScopeMode { None, Selected, All }

public enum AccessRuleStatus { Active, Inactive, Expired }