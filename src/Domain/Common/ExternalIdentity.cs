namespace CompanyAccessManagement.Domain.Common;

/// <summary>
/// Identity of an organization record in an external system of record (ERP/HR). Either both parts are present or
/// neither is. The source is normalized (trimmed, upper-case); the id is kept exactly as the external system issues it.
/// </summary>
public static class ExternalIdentity
{
    public const int MaxSourceLength = 50;
    public const int MaxIdLength = 200;

    public static string? NormalizeSource(string? source) => source?.Trim().ToUpperInvariant();

    public static (string? Source, string? Id) Normalize(string? source, string? id)
    {
        if (source is null && id is null)
            return (null, null);

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(id))
            throw new DomainRuleViolationException("EXTERNAL_IDENTITY_INCOMPLETE", "ExternalSource and ExternalId must be provided together and must not be blank.");

        var normalizedSource = NormalizeSource(source)!;
        if (normalizedSource.Length > MaxSourceLength || id.Length > MaxIdLength)
            throw new DomainRuleViolationException("EXTERNAL_IDENTITY_TOO_LONG", $"ExternalSource is limited to {MaxSourceLength} and ExternalId to {MaxIdLength} characters.");

        return (normalizedSource, id);
    }
}
