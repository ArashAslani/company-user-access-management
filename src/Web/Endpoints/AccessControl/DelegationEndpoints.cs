using CompanyAccessManagement.Application.AccessControl.Delegations.Commands;
using CompanyAccessManagement.Web.Authorization;
using CompanyAccessManagement.Web.Infrastructure;

namespace CompanyAccessManagement.Web.Endpoints.AccessControl;

public sealed class DelegationEndpoints : IEndpointGroup
{
    public static string RoutePrefix => "/api/v1/access-control/delegations";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Delegation").RequireAuthorization();

        group.MapPost("/", async (CreateDelegationCommand command, ISender sender) =>
        {
            var id = await sender.Send(command);
            return Results.Created($"{RoutePrefix}/{id}", new { id });
        })
        .RequirePermission("AccessManagement.Delegation.Create")
        .WithName("CreateDelegation")
        .Produces<Guid>(StatusCodes.Status201Created);

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender) =>
        {
            await sender.Send(new RevokeDelegationCommand(id));
            return Results.NoContent();
        })
        .RequirePermission("AccessManagement.Delegation.Revoke")
        .WithName("RevokeDelegation")
        .Produces(StatusCodes.Status204NoContent);
    }
}
