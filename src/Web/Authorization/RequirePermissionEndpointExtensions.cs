using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompanyAccessManagement.Web.Authorization;

public static class RequirePermissionEndpointExtensions
{
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission)
    {
        builder.Add(endpointBuilder =>
        {
            endpointBuilder.Metadata.Add(new RequirePermissionMetadata(permission));
        });
        return builder;
    }
}

public class RequirePermissionMetadata
{
    public RequirePermissionMetadata(string permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}
