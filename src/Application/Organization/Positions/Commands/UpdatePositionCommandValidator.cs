using CompanyAccessManagement.Application.Common.Validation;

namespace CompanyAccessManagement.Application.Organization.Positions.Commands;

public class UpdatePositionCommandValidator : AbstractValidator<UpdatePositionCommand>
{
    public UpdatePositionCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().MaximumLength(50);
        RuleFor(c => c.Title).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Description).MaximumLength(500);
        RuleFor(c => c.Status).IsInEnum();
        this.AddExternalIdentityRules();
    }
}
