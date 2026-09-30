using CompanyAccessManagement.Application.Common.Validation;

namespace CompanyAccessManagement.Application.Organization.Positions.Queries;

public class GetPositionsQueryValidator : AbstractValidator<GetPositionsQuery>
{
    public GetPositionsQueryValidator()
    {
        this.AddPaginationRules(q => q.Page, q => q.PageSize);
    }
}
