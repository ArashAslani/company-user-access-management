using CompanyAccessManagement.Domain.Common;

namespace CompanyAccessManagement.Application.Common.Validation;

/// <summary>Optional ERP/HR identity carried by organization create/update commands.</summary>
public interface IExternalIdentityFields
{
    string? ExternalSource { get; }
    string? ExternalId { get; }
}

public static class ExternalIdentityRules
{
    public const string DuplicateCode = "EXTERNAL_IDENTITY_DUPLICATE";

    public static void AddExternalIdentityRules<T>(this AbstractValidator<T> validator) where T : IExternalIdentityFields
    {
        validator.When(c => c.ExternalSource is not null || c.ExternalId is not null, () =>
        {
            validator.RuleFor(c => c.ExternalSource)
                .Must(s => !string.IsNullOrWhiteSpace(s)).WithMessage("ExternalSource is required when ExternalId is provided.")
                .MaximumLength(ExternalIdentity.MaxSourceLength);
            validator.RuleFor(c => c.ExternalId)
                .Must(s => !string.IsNullOrWhiteSpace(s)).WithMessage("ExternalId is required when ExternalSource is provided.")
                .MaximumLength(ExternalIdentity.MaxIdLength);
        });
    }

    public static DomainRuleViolationException Duplicate(string entity)
        => new(DuplicateCode, $"Another {entity} already uses this external identity.");
}
