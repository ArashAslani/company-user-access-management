using CompanyAccessManagement.Application.Common.Interfaces;
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
}
