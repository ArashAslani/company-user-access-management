namespace CleanArchitecture.Application.Common.Attachments;

public record AttachmentDto
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = null!;
    public string ContentType { get; init; } = null!;
    public long SizeBytes { get; init; }
    public string Url { get; init; } = null!;
    public DateTimeOffset UploadedAt { get; init; }
    public Guid UploadedByUserId { get; init; }
}