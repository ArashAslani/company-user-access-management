using System.Net;
using System.Security.Claims;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;

namespace CompanyAccessManagement.ApiIntegrationTests;

[TestFixture]
public class SecurityPipelineTests : ApiTestBase
{
    private Guid _companyId;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _, _, _, _) = await SeedTestDataAsync();
    }

    [Test]
    public async Task PermissionHandler_WithoutEndpointMetadata_AndWithoutPermission_DoesNotSucceed()
    {
        var session = await CreateAuthorizedClientAsync(_companyId);

        var context = await RunHandlerAsync(session.UserId, _companyId, "Organization.Position.Read");

        context.HasSucceeded.ShouldBeFalse();
    }

    [Test]
    public async Task PermissionHandler_WithoutEndpointMetadata_AuthorizesTheRequirementPermission()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Read");

        var granted = await RunHandlerAsync(session.UserId, _companyId, "Organization.Position.Read");
        var other = await RunHandlerAsync(session.UserId, _companyId, "Organization.Position.Delete");

        granted.HasSucceeded.ShouldBeTrue();
        other.HasSucceeded.ShouldBeFalse();
    }

    [Test]
    public async Task PermissionHandler_WithoutWorkspaceCompany_DoesNotSucceed()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Position.Read");

        var context = await RunHandlerAsync(session.UserId, null, "Organization.Position.Read");

        context.HasSucceeded.ShouldBeFalse();
    }

    [Test]
    public async Task Cors_NoConfiguredOrigins_ForeignOriginGetsNoAllowOriginHeader()
    {
        var response = await SendWithOriginAsync(Client, "https://evil.example");

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Test]
    public async Task Cors_ExplicitOrigin_OnlyThatOriginIsAllowed()
    {
        using var configured = Factory.WithWebHostBuilder(b => b.UseSetting("Cors:AllowedOrigins:0", "https://app.example"));
        using var client = configured.CreateClient();

        var allowed = await SendWithOriginAsync(client, "https://app.example");
        var foreign = await SendWithOriginAsync(client, "https://evil.example");

        allowed.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["https://app.example"]);
        foreign.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    private async Task<AuthorizationHandlerContext> RunHandlerAsync(Guid userId, Guid? companyId, string permission)
    {
        using var scope = Factory.Services.CreateScope();
        var handler = new PermissionAuthorizationHandler(
            scope.ServiceProvider.GetRequiredService<IAccessEvaluator>(),
            new StubUser(userId),
            new StubWorkspace(companyId));

        var requirement = new PermissionRequirement(permission);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"));
        var context = new AuthorizationHandlerContext([requirement], principal, new DefaultHttpContext());

        await handler.HandleAsync(context);
        return context;
    }

    private static Task<HttpResponseMessage> SendWithOriginAsync(HttpClient client, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/organization/positions");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return client.SendAsync(request);
    }

    private sealed class StubUser(Guid id) : IUser
    {
        public Guid? Id { get; } = id;
        public List<string>? Roles => null;
    }

    private sealed class StubWorkspace(Guid? companyId) : ICurrentWorkspace
    {
        public Guid? CompanyId { get; } = companyId;
    }
}
