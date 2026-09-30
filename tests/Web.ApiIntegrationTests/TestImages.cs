using System.Net.Http.Headers;

namespace CompanyAccessManagement.ApiIntegrationTests;

internal static class TestImages
{
    /// <summary>A real, decodable 1x1 PNG.</summary>
    public static readonly byte[] Png1x1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public static MultipartFormDataContent SignatureUpload(byte[] bytes, string contentType = "image/png", string fileName = "signature.png")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }
}
