using CompanyAccessManagement.Application.Common.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;

namespace CompanyAccessManagement.Infrastructure.Organization;

public sealed class ImageSharpSignatureValidator : ISignatureImageValidator
{
    /// <summary>Upper bound per side, checked from the header before decoding so a small file cannot claim a huge canvas.</summary>
    public const int MaxDimension = 4096;

    public SignatureImageCheck Check(byte[] content, string mimeType)
    {
        try
        {
            var info = Image.Identify(content);
            var expected = mimeType.ToLowerInvariant() switch
            {
                "image/png" => (SixLabors.ImageSharp.Formats.IImageFormat)PngFormat.Instance,
                "image/jpeg" => JpegFormat.Instance,
                _ => null
            };
            if (expected is null || info.Metadata.DecodedImageFormat != expected)
                return SignatureImageCheck.NotDecodable;

            if (info.Width > MaxDimension || info.Height > MaxDimension)
                return SignatureImageCheck.DimensionsTooLarge;

            using var image = Image.Load(content);
            return SignatureImageCheck.Valid;
        }
        catch (ImageFormatException)
        {
            return SignatureImageCheck.NotDecodable;
        }
    }
}
