namespace CompanyAccessManagement.Domain.Common;

/// <summary>
/// Raised when an operation would break a business invariant. <see cref="Code"/> is a stable,
/// machine-readable identifier (for example <c>SEALED_RECORD</c>) that the API returns to clients.
/// </summary>
public sealed class DomainRuleViolationException : Exception
{
    public DomainRuleViolationException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
