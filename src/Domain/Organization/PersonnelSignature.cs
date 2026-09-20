using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.Organization;

public sealed class PersonnelSignature : BaseAuditableEntity<Guid>
{
    public override Guid Id { get; protected set; }
    public Guid PersonnelId { get; private set; }
    public int Version { get; private set; }
    public string MimeType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public string ContentHash { get; private set; } = null!;
    public byte[] Content { get; private set; } = null!;
    public bool IsCurrent { get; private set; }
    public Guid? CreatedByUserId { get; private set; }

    public Personnel? Personnel { get; private set; }

    private PersonnelSignature() { }

    public PersonnelSignature(Guid personnelId, int version, string mimeType, long sizeBytes, string contentHash, byte[] content, Guid? createdByUserId)
    {
        Id = Guid.NewGuid();
        PersonnelId = personnelId;
        Version = version;
        MimeType = mimeType;
        SizeBytes = sizeBytes;
        ContentHash = contentHash;
        Content = content;
        IsCurrent = true;
        CreatedByUserId = createdByUserId;
        Created = DateTimeOffset.UtcNow;
    }

    public void SetNotCurrent()
    {
        IsCurrent = false;
    }
}