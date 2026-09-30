namespace CompanyAccessManagement.Application.Organization.Personnel.Commands;

internal static class PersonnelRules
{
    /// <summary>Iranian national code: exactly ten digits.</summary>
    public const string NationalCodePattern = @"^\d{10}$";
}

public class CreatePersonnelCommandValidator : AbstractValidator<CreatePersonnelCommand>
{
    public CreatePersonnelCommandValidator()
    {
        RuleFor(c => c.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.LastName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.NationalCode).NotEmpty().Matches(PersonnelRules.NationalCodePattern)
            .WithMessage("National code must be exactly 10 digits.");
        RuleFor(c => c.PhoneNumber).MaximumLength(20);
        RuleFor(c => c.Gender).IsInEnum();
        RuleFor(c => c.Status).IsInEnum();
    }
}

public class UpdatePersonnelCommandValidator : AbstractValidator<UpdatePersonnelCommand>
{
    public UpdatePersonnelCommandValidator()
    {
        RuleFor(c => c.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.LastName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.NationalCode).NotEmpty().Matches(PersonnelRules.NationalCodePattern)
            .WithMessage("National code must be exactly 10 digits.");
        RuleFor(c => c.PhoneNumber).MaximumLength(20);
        RuleFor(c => c.Gender).IsInEnum();
        RuleFor(c => c.Status).IsInEnum();
    }
}

public class AssignPositionCommandValidator : AbstractValidator<AssignPositionCommand>
{
    public AssignPositionCommandValidator()
    {
        RuleFor(c => c.PositionId).NotEmpty();
        RuleFor(c => c.EffectiveTo).GreaterThan(c => c.EffectiveFrom).When(c => c.EffectiveTo.HasValue);
    }
}

public class UpdatePositionAssignmentCommandValidator : AbstractValidator<UpdatePositionAssignmentCommand>
{
    public UpdatePositionAssignmentCommandValidator()
    {
        RuleFor(c => c.EffectiveTo).GreaterThan(c => c.EffectiveFrom).When(c => c.EffectiveTo.HasValue);
        RuleFor(c => c.Status).IsInEnum();
    }
}
