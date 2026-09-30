using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Interfaces;

namespace CompanyAccessManagement.Application.Common.Security;

/// <summary>
/// Tenant guards for handlers. The workspace company is set only after membership has been validated,
/// so it is the single source of truth; client-supplied company ids may only repeat it.
/// </summary>
public static class CurrentWorkspaceExtensions
{
    public static Guid RequireCompanyId(this ICurrentWorkspace workspace)
        => workspace.CompanyId ?? throw new ForbiddenAccessException();

    /// <summary>Returns the workspace company; a supplied company id that differs from it is rejected with 403.</summary>
    public static Guid EnsureCompany(this ICurrentWorkspace workspace, Guid? requestedCompanyId)
    {
        var companyId = workspace.RequireCompanyId();

        if (requestedCompanyId is { } requested && requested != Guid.Empty && requested != companyId)
            throw new ForbiddenAccessException();

        return companyId;
    }
}
