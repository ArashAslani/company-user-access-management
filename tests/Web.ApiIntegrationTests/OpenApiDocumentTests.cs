using NUnit.Framework;
using Shouldly;
using System.Net;
using System.Text.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

[TestFixture]
public class OpenApiDocumentTests : ApiTestBase
{
    [Test]
    public async Task IdentityEndpoints_AreDescribedAtTheirRealPaths()
    {
        using var document = await GetDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");

        paths.GetProperty("/login").GetProperty("post").GetProperty("summary").GetString().ShouldBe("Log in");
        paths.GetProperty("/refresh").GetProperty("post").GetProperty("summary").GetString().ShouldBe("Refresh token");
    }

    [Test]
    public async Task RemovedSurfaces_AreNotExposed()
    {
        using var document = await GetDocumentAsync();
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        paths.ShouldNotBeEmpty();
        paths.ShouldNotContain(p => p.StartsWith("/api/v1/attachments", StringComparison.OrdinalIgnoreCase));
        paths.ShouldNotContain(p => p.StartsWith("/api/v1/access-control/audit", StringComparison.OrdinalIgnoreCase));
        paths.ShouldNotContain(p => p.Contains("workshops", StringComparison.OrdinalIgnoreCase));
        paths.ShouldNotContain(p => p.StartsWith("/api/Users", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task IgnoredFields_AreNotPartOfTheContract()
    {
        using var document = await GetDocumentAsync();
        var root = document.RootElement;

        var createPosition = SchemaProperties(root, "CreatePositionCommand");
        createPosition.ShouldNotContain("kind");
        createPosition.ShouldNotContain("holdingId");
        createPosition.ShouldContain("description");

        SchemaProperties(root, "CreateRoleCommand").ShouldNotContain("validFrom");

        SchemaProperties(root, "BulkAssignRoleCommand").OrderBy(p => p).ShouldBe(["roleId", "userCompanyIds"]);

        var getRolesParameters = root.GetProperty("paths").GetProperty("/api/v1/access-control/roles").GetProperty("get")
            .GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
        getRolesParameters.ShouldNotBeEmpty();
        getRolesParameters.ShouldNotContain("applicationId");
    }

    private static List<string> SchemaProperties(JsonElement root, string schema) =>
        root.GetProperty("components").GetProperty("schemas").GetProperty(schema)
            .GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();

    private async Task<JsonDocument> GetDocumentAsync()
    {
        var response = await Client.GetAsync("/openapi/v1.json");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
