using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Net;
using System.Net.Http.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

/// <summary>A signature must decode as an image of its declared type; magic bytes alone are not enough.</summary>
[TestFixture]
public class SignatureValidationApiTests : ApiTestBase
{
    private AuthSession _session = null!;
    private Guid _personnelId;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        var (companyId, _, _, _, _) = await SeedTestDataAsync();
        _session = await CreateAuthorizedClientAsync(companyId, "Organization.Personnel.Create", "Organization.PersonnelSignature.Create");
        var response = await _session.Client.PostAsJsonAsync("/api/v1/organization/personnel",
            new { NationalCode = "7777777777", FirstName = "Sign", LastName = "Er", Gender = 1 });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        _personnelId = (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    [Test]
    public async Task CorruptImage_WithValidMagicBytes_IsRejected()
    {
        var corrupt = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
            .Concat(Enumerable.Range(0, 256).Select(i => (byte)i))
            .ToArray();

        await ShouldBeRejectedAsync(await UploadAsync(corrupt, "image/png"), "SIGNATURE_NOT_DECODABLE");
    }

    [Test]
    public async Task TruncatedPng_IsRejected()
    {
        await ShouldBeRejectedAsync(await UploadAsync(TestImages.Png1x1[..40], "image/png"), "SIGNATURE_NOT_DECODABLE");
    }

    [Test]
    public async Task CorruptJpeg_WithValidMagicBytes_IsRejected()
    {
        var corrupt = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.Concat(new byte[128]).ToArray();

        await ShouldBeRejectedAsync(await UploadAsync(corrupt, "image/jpeg", "signature.jpg"), "SIGNATURE_NOT_DECODABLE");
    }

    [Test]
    public async Task HugeDimensions_AreRejectedBeforeDecoding()
    {
        await ShouldBeRejectedAsync(await UploadAsync(Encode(5000, 1, jpeg: false), "image/png"), "SIGNATURE_DIMENSIONS_TOO_LARGE");
    }

    [Test]
    public async Task ValidPngAndJpeg_AreStoredAsVersions()
    {
        (await UploadAsync(TestImages.Png1x1, "image/png")).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await UploadAsync(Encode(16, 8, jpeg: true), "image/jpeg", "signature.jpg")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var stored = await WithDbAsync(db => db.PersonnelSignatures.Where(s => s.PersonnelId == _personnelId).OrderBy(s => s.Version).ToListAsync());
        stored.Select(s => s.MimeType).ShouldBe(new[] { "image/png", "image/jpeg" });
        stored.Single(s => s.IsCurrent).Version.ShouldBe(2);
    }

    private async Task ShouldBeRejectedAsync(HttpResponseMessage response, string code)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain(code);
        (await WithDbAsync(db => db.PersonnelSignatures.AnyAsync(s => s.PersonnelId == _personnelId))).ShouldBeFalse();
    }

    private Task<HttpResponseMessage> UploadAsync(byte[] bytes, string contentType, string fileName = "signature.png")
        => _session.Client.PostAsync($"/api/v1/organization/personnel/{_personnelId}/signature",
            TestImages.SignatureUpload(bytes, contentType, fileName));

    private static byte[] Encode(int width, int height, bool jpeg)
    {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream();
        if (jpeg)
            image.SaveAsJpeg(stream);
        else
            image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private record IdResponse(Guid Id);
}
