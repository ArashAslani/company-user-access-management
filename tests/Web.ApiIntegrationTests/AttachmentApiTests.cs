using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace CompanyAccessManagement.ApiIntegrationTests;

/// <summary>
/// Per-entity Position and Role attachments (API §0 / ADR-0008): only PDF/XLSX/DOCX up to 8 MB, magic-byte checked,
/// upload and delete audited, download scoped to the owning entity and company.
/// </summary>
[TestFixture]
public class AttachmentApiTests : ApiTestBase
{
    private static readonly string[] Permissions =
    [
        "Organization.Position.Create",
        "Organization.Position.Read",
        "AccessManagement.Role.Create",
        "AccessManagement.Role.Read",
        "Attachment.Create",
        "Attachment.Read",
        "Attachment.Delete"
    ];

    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF");
    private static readonly byte[] ZipBytes = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00];
    private static readonly byte[] FakePdfBytes = Encoding.ASCII.GetBytes("not-a-pdf");

    private Guid _companyId;
    private Guid _appId;
    private AuthSession _session = null!;
    private Guid _positionId;
    private Guid _roleId;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _appId, _, _, _) = await SeedTestDataAsync();
        _session = await CreateAuthorizedClientAsync(_companyId, Permissions);
        _positionId = await WithDbAsync(async db =>
        {
            var position = new Position(_companyId, "ATT-POS", "Attachment Position");
            db.Positions.Add(position);
            await db.SaveChangesAsync(default);
            return position.Id;
        });
        _roleId = await CreateRoleAsync(_companyId, _appId, "Attachment Role", "ATT-ROLE");
    }

    [Test]
    public async Task Position_UploadPdf_DownloadRoundTripsAndAudits()
    {
        var upload = await UploadAsync($"/api/v1/organization/positions/{_positionId}/attachments", PdfBytes, "application/pdf", "doc.pdf");
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await upload.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var download = await _session.Client.GetAsync($"/api/v1/organization/positions/{_positionId}/attachments/{id}");
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(PdfBytes);

        var audit = await WithDbAsync(db => db.AuditLogs.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.AttachmentUploaded));
        audit.EntityId.ShouldBe(id);
        audit.Metadata.ShouldNotBeNull();
        audit.Metadata.ShouldContain("doc.pdf");
        audit.Metadata.ShouldNotContain("%PDF");
    }

    [Test]
    public async Task Role_UploadXlsxAndDocx_Succeed()
    {
        var xlsx = await UploadAsync($"/api/v1/access-control/roles/{_roleId}/attachments", ZipBytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "sheet.xlsx");
        var docx = await UploadAsync($"/api/v1/access-control/roles/{_roleId}/attachments", ZipBytes,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "doc.docx");

        xlsx.StatusCode.ShouldBe(HttpStatusCode.Created);
        docx.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await WithDbAsync(db => db.Attachments.CountAsync(a => a.OwnerType == AttachmentOwnerType.Role && a.OwnerId == _roleId)))
            .ShouldBe(2);
    }

    [Test]
    public async Task Upload_WrongMagicBytes_Conflict()
    {
        var response = await UploadAsync($"/api/v1/organization/positions/{_positionId}/attachments", FakePdfBytes, "application/pdf", "fake.pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("ATTACHMENT_TYPE_NOT_ALLOWED");
        (await WithDbAsync(db => db.Attachments.CountAsync())).ShouldBe(0);
    }

    [Test]
    public async Task Upload_Oversize_Conflict()
    {
        var oversize = new byte[Attachment.MaxBytes + 1];
        Encoding.ASCII.GetBytes("%PDF").CopyTo(oversize, 0);

        var response = await UploadAsync($"/api/v1/organization/positions/{_positionId}/attachments", oversize, "application/pdf", "big.pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("ATTACHMENT_TOO_LARGE");
    }

    [Test]
    public async Task Upload_DisallowedMime_Conflict()
    {
        var response = await UploadAsync($"/api/v1/organization/positions/{_positionId}/attachments", TestImages.Png1x1, "image/png", "x.png");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("ATTACHMENT_TYPE_NOT_ALLOWED");
    }

    [Test]
    public async Task Download_WrongOwner_NotFound()
    {
        var id = (await (await UploadAsync($"/api/v1/organization/positions/{_positionId}/attachments", PdfBytes, "application/pdf", "doc.pdf"))
            .Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var response = await _session.Client.GetAsync($"/api/v1/access-control/roles/{_roleId}/attachments/{id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Download_CrossTenant_NotFound()
    {
        var id = (await (await UploadAsync($"/api/v1/organization/positions/{_positionId}/attachments", PdfBytes, "application/pdf", "doc.pdf"))
            .Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var otherCompany = await CreateCompanyAsync("OTHER", "Other");
        var other = await CreateAuthorizedClientAsync("other@test.com", otherCompany, Permissions);

        var response = await other.Client.GetAsync($"/api/v1/organization/positions/{_positionId}/attachments/{id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Delete_RemovesRowAndWritesAudit()
    {
        var id = (await (await UploadAsync($"/api/v1/organization/positions/{_positionId}/attachments", PdfBytes, "application/pdf", "doc.pdf"))
            .Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var response = await _session.Client.DeleteAsync($"/api/v1/organization/positions/{_positionId}/attachments/{id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await WithDbAsync(db => db.Attachments.AnyAsync(a => a.Id == id))).ShouldBeFalse();
        var audit = await WithDbAsync(db => db.AuditLogs.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.AttachmentDeleted));
        audit.EntityId.ShouldBe(id);
        audit.Metadata!.ShouldContain("doc.pdf");
    }

    private Task<HttpResponseMessage> UploadAsync(string url, byte[] content, string mimeType, string fileName)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        return _session.Client.PostAsync(url, form);
    }

    private sealed record IdResponse(Guid Id);
}
