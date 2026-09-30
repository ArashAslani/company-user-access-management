using System.Security.Cryptography;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using MediatR;
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

    public UploadSignatureCommandHandler(IApplicationDbContext context, IUser user, ICurrentWorkspace workspace)
    {
        _context = context;
        _user = user;
        _workspace = workspace;
    }

    public async Task<Guid> Handle(UploadSignatureCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var personnel = await _context.Personnel
            .VisibleIn(companyId)
            .Include(p => p.Signatures)
            .FirstOrDefaultAsync(p => p.Id == request.PersonnelId, cancellationToken);

        Guard.Against.NotFound(request.PersonnelId, personnel);

        var contentHash = Convert.ToHexString(SHA256.HashData(request.Content));

        var signature = personnel.UploadSignature(request.Content, request.ContentType, contentHash, _user.Id);

        await _context.SaveChangesAsync(cancellationToken);

        return signature.Id;
    }
}
