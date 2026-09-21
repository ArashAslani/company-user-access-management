using CleanArchitecture.Application.AccessControl.Scopes.Queries;
using CleanArchitecture.Web.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitecture.Web.Endpoints.AccessControl;

public static class ScopeEndpoints
{
    public static void MapScopeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/access-control/scopes")
            .WithTags("Scope")
            .RequireAuthorization();

        // Get workshops for scope selection
        group.MapGet("/workshops", async (
            ISender sender,
            [FromQuery] Guid companyId) =>
        {
            var result = await sender.Send(new GetWorkshopsQuery { CompanyId = companyId });
            return Results.Ok(result);
        })
        .RequirePermission("RuleScope.Read")
        .WithName("GetWorkshops")
        .Produces<List<WorkshopDto>>(StatusCodes.Status200OK);

        // Get resource tree for permission picker
        group.MapGet("/resources/tree", async (
            ISender sender,
            [FromQuery] Guid applicationId) =>
        {
            var result = await sender.Send(new GetResourceTreeQuery { ApplicationId = applicationId });
            return Results.Ok(result);
        })
        .RequirePermission("Resource.Read")
        .WithName("GetResourceTree")
        .Produces<List<ScopeResourceTreeDto>>(StatusCodes.Status200OK);
    }
}