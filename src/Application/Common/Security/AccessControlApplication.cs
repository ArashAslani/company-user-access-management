using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.AccessControl;

namespace CompanyAccessManagement.Application.Common.Security;

/// <summary>
/// The single application whose permissions govern this API. Roles and resources of any other application are
/// invisible to administration endpoints, which answer 404 for them.
/// </summary>
public static class AccessControlApplication
{
    public const string Code = "QC";

    public static IQueryable<Role> InAccessControlApplication(this IQueryable<Role> roles, IApplicationDbContext context)
        => roles.Where(r => context.Applications.Any(a => a.Id == r.ApplicationId && a.Code == Code));

    public static Task<bool> IsAccessControlApplicationAsync(this IApplicationDbContext context, Guid applicationId, CancellationToken cancellationToken)
        => context.Applications.AnyAsync(a => a.Id == applicationId && a.Code == Code, cancellationToken);
}
