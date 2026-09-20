using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Web.Services;
using Microsoft.AspNetCore.Authorization;

namespace CleanArchitecture.Web.Authorization;

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

        if (userId == null || companyId == null)
        {
            context.Fail();
            return;
        }

        var decision = await _accessEvaluator.EvaluateAsync(
            new AccessRequest(userId.Value, companyId.Value, "QC", requirement.Permission));

        if (decision.Allowed)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }
    }
}