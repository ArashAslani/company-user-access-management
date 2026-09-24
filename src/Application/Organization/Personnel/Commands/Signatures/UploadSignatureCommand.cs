using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.Organization;
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

    public UploadSignatureCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Guid> Handle(UploadSignatureCommand request, CancellationToken cancellationToken)
    {
        var personnel = await _context.Personnel
            .Include(p => p.Signatures)
            .FirstOrDefaultAsync(p => p.Id == request.PersonnelId, cancellationToken);

        if (personnel == null)
            throw new InvalidOperationException("Personnel not found.");

        // Validate file size (max 8MB)
        if (request.Content.Length > 8 * 1024 * 1024)
            throw new InvalidOperationException("Signature file exceeds 8MB limit.");

        // Validate content type
        var allowedTypes = new[] { "image/png", "image/jpeg" };
        if (!allowedTypes.Contains(request.ContentType.ToLowerInvariant()))
            throw new InvalidOperationException("Only PNG/JPEG signatures are allowed.");

        // Compute content hash
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(request.Content));

        var signature = personnel.UploadSignature(request.Content, request.ContentType, contentHash, Guid.Empty);
        
        await _context.SaveChangesAsync(cancellationToken);

        return signature.Id;
    }
}