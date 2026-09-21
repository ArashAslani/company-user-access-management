using CleanArchitecture.Application.Common.Attachments;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.Common.Attachments.Commands;

public record UploadAttachmentCommand : IRequest<AttachmentDto>
{
    public string FileName { get; init; } = null!;
    public string ContentType { get; init; } = null!;
    public byte[] Content { get; init; } = null!;
    public Guid EntityType { get; init; }
    public Guid EntityId { get; init; }
    public string Description { get; init; } = null!;
}

public class UploadAttachmentCommandHandler : IRequestHandler<UploadAttachmentCommand, AttachmentDto>
{
    private readonly IApplicationDbContext _context;

    public UploadAttachmentCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AttachmentDto> Handle(UploadAttachmentCommand request, CancellationToken cancellationToken)
    {
        // Validate file size (max 8MB)
        if (request.Content.Length > 8 * 1024 * 1024)
            throw new InvalidOperationException("File size exceeds 8MB limit.");

        // Validate content type
        var allowedTypes = new[] { "application/pdf", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "image/png", "image/jpeg" };
        
        if (!allowedTypes.Contains(request.ContentType.ToLowerInvariant()))
            throw new InvalidOperationException("File type not allowed.");

        var attachment = new Attachment(Guid.NewGuid())
        {
            FileName = request.FileName,
            ContentType = request.ContentType,
            SizeBytes = request.Content.Length,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            Description = request.Description,
            UploadedByUserId = Guid.Empty,
            UploadedAt = DateTimeOffset.UtcNow
        };

        // _context.Attachments.Add(attachment);
        // await _context.SaveChangesAsync(cancellationToken);

        return new AttachmentDto
        {
            Id = attachment.Id,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            SizeBytes = attachment.SizeBytes,
            Url = $"/api/v1/attachments/{attachment.Id}",
            UploadedAt = attachment.UploadedAt,
            UploadedByUserId = attachment.UploadedByUserId
        };
    }
}

public class Attachment : BaseEntity<Guid>
{
    public override Guid Id { get; protected set; }
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long SizeBytes { get; set; }
    public Guid EntityType { get; set; }
    public Guid EntityId { get; set; }
    public string Description { get; set; } = null!;
    public Guid UploadedByUserId { get; set; }
    public DateTimeOffset UploadedAt { get; set; }

    public Attachment(Guid id)
    {
        Id = id;
    }
}