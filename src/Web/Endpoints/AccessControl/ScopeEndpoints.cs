using CompanyAccessManagement.Application.AccessControl.Scopes.Queries;
using CompanyAccessManagement.Web.Authorization;
using CompanyAccessManagement.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CompanyAccessManagement.Web.Endpoints.AccessControl;

public sealed class ScopeEndpoints : IEndpointGroup
{
    public static string RoutePrefix => "/api/v1/access-control/scopes";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Scope")
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