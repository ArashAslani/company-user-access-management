using CompanyAccessManagement.Domain.AccessControl;

namespace CompanyAccessManagement.Application.Common.Interfaces;

/// <summary>
/// Admin Authority (design §32) for the current user in the workspace company. A grant is allowed only when one single
/// managing role held by the actor covers it entirely; authority is never combined across roles. Valid company or
/// global super-admins are exempt. Failures throw <c>ForbiddenAccessException</c>.
/// </summary>
public interface IAdminAuthority
{
    /// <summary>The target role must be a strict descendant of the managing role, whose branch must effectively allow every grant within its scope.</summary>
    Task EnsureCanGrantAsync(Guid targetRoleId, IReadOnlyCollection<AuthorityGrant> grants, CancellationToken cancellationToken);

    /// <summary>The role being assigned must be a strict descendant of a managing role.</summary>
    Task EnsureCanAssignAsync(Guid roleId, CancellationToken cancellationToken);
}

/// <param name="Scopes">Used when <paramref name="ScopeMode"/> is <see cref="ScopeMode.Selected"/>; each scope is checked on its own.</param>
public sealed record AuthorityGrant(Guid PermissionId, ScopeMode ScopeMode, IReadOnlyCollection<AuthorityScope>? Scopes = null);

public sealed record AuthorityScope(string ScopeType, string ScopeKey);
