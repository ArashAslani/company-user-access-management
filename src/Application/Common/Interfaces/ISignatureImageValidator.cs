namespace CompanyAccessManagement.Application.Common.Interfaces;

/// <summary>Decodes an uploaded signature to prove it is a real image of the declared type. Never logs the content.</summary>
public interface ISignatureImageValidator
{
    SignatureImageCheck Check(byte[] content, string mimeType);
}

public enum SignatureImageCheck { Valid, NotDecodable, DimensionsTooLarge }
