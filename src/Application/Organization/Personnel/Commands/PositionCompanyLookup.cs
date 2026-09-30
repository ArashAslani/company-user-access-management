using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.Common;
using CompanyAccessManagement.Domain.Organization;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Personnel.Commands;

internal static class PositionCompanyLookup
{
    /// <summary>
    /// Loads the owning company of every position the personnel is assigned to, plus <paramref name="extraPositionId"/>.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, Guid>> LoadAsync(
        IApplicationDbContext context,
        Domain.Organization.Personnel personnel,
        Guid extraPositionId,
        CancellationToken cancellationToken)
    {
        var positionIds = personnel.Positions.Select(p => p.PositionId).Append(extraPositionId).Distinct().ToList();

        return await context.Positions
            .Where(p => positionIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.CompanyId, cancellationToken);
    }

    public static async Task EnsurePositionActiveAsync(IApplicationDbContext context, Guid positionId, CancellationToken cancellationToken)
    {
        if (await context.Positions.AnyAsync(p => p.Id == positionId && p.Status == PositionStatus.Inactive, cancellationToken))
            throw new DomainRuleViolationException("POSITION_INACTIVE", "Personnel cannot be assigned to an inactive position.");
    }
}
