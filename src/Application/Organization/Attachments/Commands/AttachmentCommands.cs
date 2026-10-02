using System.Security.Cryptography;
using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.Organization;

namespace CompanyAccessManagement.Application.Organization.Attachments.Commands;

public record UploadAttachmentCommand : IRequest<Guid>
{
    public AttachmentOwnerType OwnerType { get; init; }
    public Guid OwnerId { get; init; }
    public string FileName { get; init; } = null!;
    public string MimeType { get; init; } = null!;
    public byte[] Content { get; init; } = [];
}

public sealed class UploadAttachmentCommandValidator : AbstractValidator<UploadAttachmentCommand>
{
    public UploadAttachmentCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.OwnerType).IsInEnum();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.MimeType).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Content).NotEmpty();
    }
}

public sealed class UploadAttachmentCommandHandler : IRequestHandler<UploadAttachmentCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;
    private readonly IUser _user;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditWriter _audit;

    public UploadAttachmentCommandHandler(
        IApplicationDbContext context,
        ICurrentWorkspace workspace,
        IUser user,
        TimeProvider timeProvider,
        IAuditWriter audit)
    {
        _context = context;
        _workspace = workspace;
        _user = user;
        _timeProvider = timeProvider;
        _audit = audit;
    }

    public async Task<Guid> Handle(UploadAttachmentCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();
        await EnsureOwnerAsync(request.OwnerType, request.OwnerId, companyId, cancellationToken);
        Attachment.EnsureContentAllowed(request.Content, request.MimeType);
        var hash = Convert.ToHexString(SHA256.HashData(request.Content)).ToLowerInvariant();
        var attachment = new Attachment(
            request.OwnerType,
            request.OwnerId,
            companyId,
            request.FileName,
            request.MimeType,
            hash,
            request.Content,
            _timeProvider.GetUtcNow().UtcDateTime,
            _user.Id);
        _context.Attachments.Add(attachment);
        _audit.Write(new AuditWriteRequest(
            AuditEventTypes.AttachmentUploaded,
            nameof(Attachment),
            attachment.Id,
            Metadata: Metadata(attachment),
            CompanyId: companyId));
        await _context.SaveChangesAsync(cancellationToken);
        return attachment.Id;
    }

    private async Task EnsureOwnerAsync(AttachmentOwnerType ownerType, Guid ownerId, Guid companyId, CancellationToken cancellationToken)
    {
        var exists = ownerType switch
        {
            AttachmentOwnerType.Position => await _context.Positions.AnyAsync(p => p.Id == ownerId && p.CompanyId == companyId, cancellationToken),
            AttachmentOwnerType.Role => await _context.Roles.AnyAsync(r => r.Id == ownerId && r.CompanyId == companyId, cancellationToken),
            _ => false
        };
        if (!exists)
            throw new NotFoundException(ownerId.ToString(), ownerType.ToString());
    }

    internal static string Metadata(Attachment attachment) => AuditJson.Serialize(new
    {
        attachment.FileName,
        attachment.MimeType,
        attachment.SizeBytes,
        attachment.ContentHash
    });
}

public sealed record DeleteAttachmentCommand(AttachmentOwnerType OwnerType, Guid OwnerId, Guid AttachmentId) : IRequest;

public sealed class DeleteAttachmentCommandHandler : IRequestHandler<DeleteAttachmentCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;
    private readonly IAuditWriter _audit;

    public DeleteAttachmentCommandHandler(IApplicationDbContext context, ICurrentWorkspace workspace, IAuditWriter audit)
    {
        _context = context;
        _workspace = workspace;
        _audit = audit;
    }

    public async Task Handle(DeleteAttachmentCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();
        var attachment = await _context.Attachments.FirstOrDefaultAsync(a =>
            a.Id == request.AttachmentId
            && a.OwnerType == request.OwnerType
            && a.OwnerId == request.OwnerId
            && a.CompanyId == companyId, cancellationToken);
        Guard.Against.NotFound(request.AttachmentId, attachment);

        _audit.Write(new AuditWriteRequest(
            AuditEventTypes.AttachmentDeleted,
            nameof(Attachment),
            attachment.Id,
            Metadata: UploadAttachmentCommandHandler.Metadata(attachment),
            CompanyId: companyId));
        _context.Attachments.Remove(attachment);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
