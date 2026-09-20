using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.AccessControl;

public sealed class RuleScope : BaseEntity
{
    public override Guid Id { get; protected set; }
    public Guid AccessRuleId { get; private set; }
    public string ScopeType { get; private set; } = null!;
    public string ScopeKey { get; private set; } = null!;

    public AccessRule? AccessRule { get; private set; }

    private RuleScope() { }

    public RuleScope(Guid accessRuleId, string scopeType, string scopeKey)
    {
        Id = Guid.NewGuid();
        AccessRuleId = accessRuleId;
        ScopeType = scopeType;
        ScopeKey = scopeKey;
    }
}