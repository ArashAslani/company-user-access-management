using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CleanArchitecture.Web.Authorization;

public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IAccessEvaluator _accessEvaluator;
    private readonly IUser _currentUser;
    private readonly ICurrentWorkspace _currentWorkspace;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PermissionAuthorizationHandler(
        IAccessEvaluator accessEvaluator,
        IUser currentUser,
        ICurrentWorkspace currentWorkspace,
        IHttpContextAccessor httpContextAccessor)
    {
        _accessEvaluator = accessEvaluator;
        _currentUser = currentUser;
        _currentWorkspace = currentWorkspace;
        _httpContextAccessor = httpContextAccessor;
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

        // Check if endpoint has RequirePermissionMetadata
        var endpoint = _httpContextAccessor.HttpContext?.GetEndpoint();
        var permissionMetadata = endpoint?.Metadata.GetMetadata<RequirePermissionMetadata>();

        // If no metadata, allow (for endpoints without explicit permission)
        if (permissionMetadata == null)
        {
            context.Succeed(requirement);
            return;
        }

        var decision = await _accessEvaluator.EvaluateAsync(
            new AccessRequest(userId.Value, companyId.Value, "QC", permissionMetadata.Permission));

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