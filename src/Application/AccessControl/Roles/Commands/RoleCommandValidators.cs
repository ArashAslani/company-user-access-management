using CompanyAccessManagement.Application.AccessControl.Roles.Commands.BulkAssignRole;

namespace CompanyAccessManagement.Application.AccessControl.Roles.Commands;

public class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(c => c.ApplicationId).NotEmpty();
        RuleFor(c => c.Code).NotEmpty().MaximumLength(50);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Description).MaximumLength(500);
        RuleFor(c => c.Kind).IsInEnum();
        RuleFor(c => c.Status).IsInEnum();
    }
}

public class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().MaximumLength(50);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Description).MaximumLength(500);
        RuleFor(c => c.Kind).IsInEnum();
        RuleFor(c => c.Status).IsInEnum();
    }
}

public class CopyRolePermissionsCommandValidator : AbstractValidator<CopyRolePermissionsCommand>
{
    public CopyRolePermissionsCommandValidator()
    {
        RuleFor(c => c.SourceRoleId).NotEmpty();
        RuleFor(c => c.Mode).Must(m => m is CopyModes.Append or CopyModes.Replace)
            .WithMessage($"Mode must be {CopyModes.Append} or {CopyModes.Replace}.");
    }
}

public class BulkAssignRoleCommandValidator : AbstractValidator<BulkAssignRoleCommand>
{
    public BulkAssignRoleCommandValidator()
    {
        RuleFor(c => c.RoleId).NotEmpty();
        RuleFor(c => c.UserCompanyIds).NotEmpty();
        RuleForEach(c => c.UserCompanyIds).NotEmpty();
    }
}
