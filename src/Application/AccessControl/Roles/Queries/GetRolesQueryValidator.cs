using CompanyAccessManagement.Application.Common.Validation;

namespace CompanyAccessManagement.Application.AccessControl.Roles.Queries;

public class GetRolesQueryValidator : AbstractValidator<GetRolesQuery>
{
    public GetRolesQueryValidator()
    {
        this.AddPaginationRules(q => q.Page, q => q.PageSize);
    }
}
