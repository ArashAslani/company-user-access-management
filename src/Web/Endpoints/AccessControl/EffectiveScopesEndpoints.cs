using CompanyAccessManagement.Application.AccessControl.EffectiveScopes.Queries;
using CompanyAccessManagement.Web.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyAccessManagement.Web.Endpoints.AccessControl;

public sealed class EffectiveScopesEndpoints : IEndpointGroup
{
    public static string RoutePrefix => "/api/v1/access-control/effective-scopes";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("EffectiveScopes")
            .RequireAuthorization();

        group.MapGet("/", async (
            HttpContext http,
            [FromQuery] string applicationCode,
            [FromQuery] string permissionCode,
            [FromQuery] string scopeType,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetEffectiveScopesQuery(applicationCode, permissionCode, scopeType), cancellationToken);
            var etag = "\"" + result.Revision + "\"";
            http.Response.Headers.ETag = etag;
            if (IsNotModified(http.Request, etag))
                return Results.StatusCode(StatusCodes.Status304NotModified);

            return Results.Ok(result);
        })
        .RequirePermission("AccessManagement.EffectiveScopes.Read")
        .WithName("GetEffectiveScopes")
        .Produces<EffectiveScopesDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status304NotModified)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static bool IsNotModified(HttpRequest request, string quotedEtag)
    {
        if (!request.Headers.TryGetValue("If-None-Match", out var header) || string.IsNullOrWhiteSpace(header))
            return false;

        foreach (var part in header.ToString().Split(','))
        {
            var token = part.Trim();
            if (token == "*" || token == quotedEtag || token == "W/" + quotedEtag)
                return true;
        }

        return false;
    }
}
