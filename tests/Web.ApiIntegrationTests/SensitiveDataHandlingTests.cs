using System.Collections.Concurrent;
using CompanyAccessManagement.Domain.Organization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Shouldly;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

/// <summary>
/// Request payloads (PII, signature bytes) must never reach logs, and oversized uploads are rejected before buffering.
/// </summary>
[TestFixture]
public class SensitiveDataHandlingTests : ApiTestBase
{
    private const string NationalCode = "9876543210";
    private const string PhoneNumber = "0912-555-7788";

    private readonly CapturingLoggerProvider _logs = new();
    private WebApplicationFactory<Program> _rootFactory = null!;
    private Guid _companyId;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        _rootFactory = Factory;
        Factory = _rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(_logs)));
        Client.Dispose();
        Client = Factory.CreateClient();
        (_companyId, _, _, _, _) = await SeedTestDataAsync();
    }

    [TearDown]
    public override void TearDown()
    {
        base.TearDown();
        _rootFactory.Dispose();
    }

    [Test]
    public async Task PersonnelAndSignatureRequests_DoNotLogPayloads()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Create", "Organization.PersonnelSignature.Create");
        var personnelId = await CreatePersonnelAsync(session);

        var signature = TestImages.Png1x1;
        var upload = await UploadAsync(session, personnelId, signature);
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);

        var logged = _logs.Messages;
        logged.ShouldContain(m => m.Contains("CreatePersonnelCommand"));
        logged.ShouldContain(m => m.Contains("UploadSignatureCommand"));

        var base64 = Convert.ToBase64String(signature);
        var hex = Convert.ToHexString(signature);
        logged.ShouldAllBe(m => !m.Contains(NationalCode) && !m.Contains(PhoneNumber) && !m.Contains(base64) && !m.Contains(hex, StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task OversizedSignature_RejectedWithoutStoring()
    {
        var session = await CreateAuthorizedClientAsync(_companyId, "Organization.Personnel.Create", "Organization.PersonnelSignature.Create");
        var personnelId = await CreatePersonnelAsync(session);

        var oversized = new byte[Personnel.MaxSignatureBytes + 1];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(oversized, 0);

        var response = await UploadAsync(session, personnelId, oversized);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("SIGNATURE_TOO_LARGE");
        (await WithDbAsync(db => db.PersonnelSignatures.AnyAsync(s => s.PersonnelId == personnelId))).ShouldBeFalse();
    }

    private static async Task<Guid> CreatePersonnelAsync(AuthSession session)
    {
        var response = await session.Client.PostAsJsonAsync("/api/v1/organization/personnel",
            new { NationalCode, FirstName = "Sensitive", LastName = "Person", Gender = 1, PhoneNumber });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private static Task<HttpResponseMessage> UploadAsync(AuthSession session, Guid personnelId, byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var content = new MultipartFormDataContent { { file, "file", "signature.png" } };
        return session.Client.PostAsync($"/api/v1/organization/personnel/{personnelId}/signature", content);
    }

    private record IdResponse(Guid Id);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyList<string> Messages => _messages.ToList();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var structured = state is IEnumerable<KeyValuePair<string, object?>> values
                    ? string.Join(";", values.Select(v => $"{v.Key}={v.Value}"))
                    : string.Empty;
                messages.Enqueue($"{formatter(state, exception)} {structured} {exception}");
            }
        }
    }
}
