using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.Organization;

namespace CompanyAccessManagement.Application.Organization.Personnel;

/// <summary>
/// Personnel has no company of its own (design doc 13.2); company membership comes only from positions.
/// A person is visible in a workspace when they hold an active assignment to one of its positions,
/// or hold no active assignment at all (the unassigned registration pool).
/// </summary>
internal static class PersonnelWorkspaceScope
{
    public static IQueryable<Domain.Organization.Personnel> VisibleIn(
        this IQueryable<Domain.Organization.Personnel> query, IApplicationDbContext context, Guid companyId)
        => query.Where(p =>
            !context.PersonnelPositions.Any(pp => pp.PersonnelId == p.Id && pp.Status == PersonnelPositionStatus.Active)
            || context.PersonnelPositions.Any(pp => pp.PersonnelId == p.Id
                && pp.Status == PersonnelPositionStatus.Active
                && context.Positions.Any(pos => pos.Id == pp.PositionId && pos.CompanyId == companyId)));

    public static async Task<bool> IsPositionInCompanyAsync(
        IApplicationDbContext context, Guid positionId, Guid companyId, CancellationToken cancellationToken)
        => await context.Positions.AnyAsync(p => p.Id == positionId && p.CompanyId == companyId, cancellationToken);
}
