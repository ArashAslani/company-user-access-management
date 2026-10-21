namespace CompanyAccessManagement.Application.Common.Security;

public sealed class EffectiveScopesOptions
{
    public const string SectionName = "EffectiveScopes";

    public const int DefaultMaxKeys = 5000;

    public int MaxKeys { get; set; } = DefaultMaxKeys;
}
