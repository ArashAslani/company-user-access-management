using CompanyAccessManagement.Application.Common.Interfaces;

namespace CompanyAccessManagement.Application.Organization.Personnel;

/// <summary>
/// Personnel belong to exactly one company (ADR-0006) and are visible only in that company's workspace,
/// whatever their assignment state.
/// </summary>
internal static class PersonnelWorkspaceScope
{
    public static IQueryable<Domain.Organization.Personnel> VisibleIn(
        this IQueryable<Domain.Organization.Personnel> query, Guid companyId)
        => query.Where(p => p.CompanyId == companyId);

    public static async Task<bool> IsPositionInCompanyAsync(
        IApplicationDbContext context, Guid positionId, Guid companyId, CancellationToken cancellationToken)
        => await context.Positions.AnyAsync(p => p.Id == positionId && p.CompanyId == companyId, cancellationToken);
}
