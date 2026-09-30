using CompanyAccessManagement.Application.Common.Validation;

namespace CompanyAccessManagement.Application.Organization.Personnel.Queries;

public class GetPersonnelQueryValidator : AbstractValidator<GetPersonnelQuery>
{
    public GetPersonnelQueryValidator()
    {
        this.AddPaginationRules(q => q.Page, q => q.PageSize);
    }
}
