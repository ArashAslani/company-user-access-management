using System.Security.Cryptography;
using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.Common;
using MediatR;
using PersonnelEntity = CompanyAccessManagement.Domain.Organization.Personnel;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Personnel.Commands.Signatures;

public record UploadSignatureCommand : IRequest<Guid>
{
    public Guid PersonnelId { get; init; }
    public string FileName { get; init; } = null!;
    public string ContentType { get; init; } = null!;
    public byte[] Content { get; init; } = null!;
}

public class UploadSignatureCommandHandler : IRequestHandler<UploadSignatureCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _user;
    private readonly ICurrentWorkspace _workspace;
    private readonly ISignatureImageValidator _imageValidator;
    private readonly IAuditWriter _audit;

    public UploadSignatureCommandHandler(IApplicationDbContext context, IUser user, ICurrentWorkspace workspace, ISignatureImageValidator imageValidator, IAuditWriter audit)
    {
        _context = context;
        _user = user;
        _workspace = workspace;
        _imageValidator = imageValidator;
        _audit = audit;
    }

    public async Task<Guid> Handle(UploadSignatureCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var personnel = await _context.Personnel
            .VisibleIn(companyId)
            .Include(p => p.Signatures)
            .FirstOrDefaultAsync(p => p.Id == request.PersonnelId, cancellationToken);

        Guard.Against.NotFound(request.PersonnelId, personnel);

        PersonnelEntity.EnsureSignatureContentAllowed(request.Content, request.ContentType);
        switch (_imageValidator.Check(request.Content, request.ContentType))
        {
            case SignatureImageCheck.NotDecodable:
                throw new DomainRuleViolationException("SIGNATURE_NOT_DECODABLE", "The signature file is not a valid image of the declared type.");
            case SignatureImageCheck.DimensionsTooLarge:
                throw new DomainRuleViolationException("SIGNATURE_DIMENSIONS_TOO_LARGE", "The signature image dimensions are too large.");
        }

        var contentHash = Convert.ToHexString(SHA256.HashData(request.Content));

        var oldSignature = personnel.GetCurrentSignature();
        var signature = personnel.UploadSignature(request.Content, request.ContentType, contentHash, _user.Id);

        _audit.Write(new AuditWriteRequest(
            oldSignature is null ? AuditEventTypes.SignatureUploaded : AuditEventTypes.SignatureReplaced,
            "PersonnelSignature",
            signature.Id,
            Metadata: AuditJson.Serialize(new
            {
                OldSignatureId = oldSignature?.Id,
                NewSignatureId = signature.Id,
                OldHash = oldSignature?.ContentHash,
                NewHash = signature.ContentHash
            }),
            CompanyId: companyId));
        await _context.SaveChangesAsync(cancellationToken);

        return signature.Id;
    }
}
