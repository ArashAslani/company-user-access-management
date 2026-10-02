using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.Organization;

namespace CompanyAccessManagement.Application.Organization.Attachments.Queries;

public sealed record DownloadAttachmentQuery(
    AttachmentOwnerType OwnerType,
    Guid OwnerId,
    Guid AttachmentId) : IRequest<AttachmentDownloadDto>;

public sealed record AttachmentDownloadDto(byte[] Content, string MimeType, string FileName);

public sealed class DownloadAttachmentQueryHandler : IRequestHandler<DownloadAttachmentQuery, AttachmentDownloadDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;

    public DownloadAttachmentQueryHandler(IApplicationDbContext context, ICurrentWorkspace workspace)
    {
        _context = context;
        _workspace = workspace;
    }

    public async Task<AttachmentDownloadDto> Handle(DownloadAttachmentQuery request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();
        var attachment = await _context.Attachments.AsNoTracking()
            .Where(a => a.Id == request.AttachmentId
                && a.OwnerType == request.OwnerType
                && a.OwnerId == request.OwnerId
                && a.CompanyId == companyId)
            .Select(a => new AttachmentDownloadDto(a.Content, a.MimeType, a.FileName))
            .FirstOrDefaultAsync(cancellationToken);
        Guard.Against.NotFound(request.AttachmentId, attachment);
        return attachment;
    }
}
