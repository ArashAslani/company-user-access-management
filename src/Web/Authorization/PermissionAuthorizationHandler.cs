using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;

namespace CompanyAccessManagement.Web.Authorization;

/// <summary>
/// Authorizes a <see cref="PermissionRequirement"/> by evaluating its permission for the current user in the
/// workspace company. Endpoint metadata is not consulted, so a missing or mismatched attribute can never grant access.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IAccessEvaluator _accessEvaluator;
    private readonly IUser _currentUser;
    private readonly ICurrentWorkspace _currentWorkspace;

    public PermissionAuthorizationHandler(
        IAccessEvaluator accessEvaluator,
        IUser currentUser,
        ICurrentWorkspace currentWorkspace)
    {
        _accessEvaluator = accessEvaluator;
        _currentUser = currentUser;
        _currentWorkspace = currentWorkspace;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userId = _currentUser.Id;
        var companyId = _currentWorkspace.CompanyId;

        if (userId == null || companyId == null || string.IsNullOrWhiteSpace(requirement.Permission))
        {
            context.Fail();
            return;
        }

        var decision = await _accessEvaluator.EvaluateAsync(
            new AccessRequest(userId.Value, companyId.Value, AccessControlApplication.Code, requirement.Permission));

        if (decision.Allowed)
            context.Succeed(requirement);
        else
            context.Fail();
    }
}
