using CompanyAccessManagement.Domain.Common;

namespace CompanyAccessManagement.Domain.Organization;

/// <summary>File attached to a Position or Role (PDF/XLSX/DOCX). Content is stored; binary is never written to AuditLog.</summary>
public sealed class Attachment : BaseAuditableEntity<Guid>
{
    public const int MaxBytes = 8 * 1024 * 1024;

    public override Guid Id { get; protected set; }
    public AttachmentOwnerType OwnerType { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string FileName { get; private set; } = null!;
    public string MimeType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public string ContentHash { get; private set; } = null!;
    public byte[] Content { get; private set; } = null!;
    public DateTime UploadedAt { get; private set; }
    public Guid? UploadedByUserId { get; private set; }

    private Attachment() { }

    public Attachment(
        AttachmentOwnerType ownerType,
        Guid ownerId,
        Guid companyId,
        string fileName,
        string mimeType,
        string contentHash,
        byte[] content,
        DateTime uploadedAt,
        Guid? uploadedByUserId)
    {
        EnsureContentAllowed(content, mimeType);

        Id = Guid.NewGuid();
        OwnerType = ownerType;
        OwnerId = ownerId;
        CompanyId = companyId;
        FileName = fileName;
        MimeType = mimeType.ToLowerInvariant();
        SizeBytes = content.Length;
        ContentHash = contentHash;
        Content = content;
        UploadedAt = uploadedAt;
        UploadedByUserId = uploadedByUserId;
    }

    /// <summary>Size, declared MIME and magic bytes for PDF / Office Open XML (ZIP).</summary>
    public static void EnsureContentAllowed(byte[] content, string mimeType)
    {
        if (content.Length == 0)
            throw new DomainRuleViolationException("ATTACHMENT_EMPTY", "Attachment file is empty.");

        if (content.Length > MaxBytes)
            throw new DomainRuleViolationException("ATTACHMENT_TOO_LARGE", "Attachment exceeds the 8MB limit.");

        var matches = mimeType.ToLowerInvariant() switch
        {
            "application/pdf" => content.AsSpan().StartsWith("%PDF"u8),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                or "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                => content.AsSpan().StartsWith("PK"u8),
            _ => false
        };

        if (!matches)
            throw new DomainRuleViolationException("ATTACHMENT_TYPE_NOT_ALLOWED", "Only PDF, XLSX and DOCX whose content matches the declared type are allowed.");
    }
}

public enum AttachmentOwnerType { Position = 0, Role = 1 }
