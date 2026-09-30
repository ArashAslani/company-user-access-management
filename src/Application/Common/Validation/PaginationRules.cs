using System.Linq.Expressions;

namespace CompanyAccessManagement.Application.Common.Validation;

public static class PaginationRules
{
    public const int MaxPageSize = 100;

    public static void AddPaginationRules<T>(this AbstractValidator<T> validator, Expression<Func<T, int>> page, Expression<Func<T, int>> pageSize)
    {
        validator.RuleFor(page).GreaterThanOrEqualTo(1);
        validator.RuleFor(pageSize).InclusiveBetween(1, MaxPageSize);
    }
}
