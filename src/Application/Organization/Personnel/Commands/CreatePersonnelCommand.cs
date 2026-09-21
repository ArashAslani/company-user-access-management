using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.Organization.Personnel.Commands;

public record CreatePersonnelCommand : IRequest<Guid>
{
    public string FirstName { get; init; } = null!;
    public string LastName { get; init; } = null!;
    public string NationalCode { get; init; } = null!;
    public Gender Gender { get; init; }
    public string? PhoneNumber { get; init; }
    public Guid CompanyId { get; init; }
    public PersonnelStatus Status { get; init; } = PersonnelStatus.Draft;
    public Guid[] AttachmentIds { get; init; } = [];
}

public class CreatePersonnelCommandHandler : IRequestHandler<CreatePersonnelCommand, Guid>
{
    private readonly IApplicationDbContext _context;

    public CreatePersonnelCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Guid> Handle(CreatePersonnelCommand request, CancellationToken cancellationToken)
    {
        // Check duplicate national code
        var exists = await _context.Personnel
            .AnyAsync(p => p.NationalCode == request.NationalCode, cancellationToken);

        if (exists)
            throw new InvalidOperationException("DUPLICATE_NATIONAL_CODE");

        var personnel = new CleanArchitecture.Domain.Organization.Personnel(
            request.NationalCode, 
            request.FirstName, 
            request.LastName, 
            request.Gender, 
            null, 
            request.PhoneNumber);

        // Note: Personnel is created in Draft status, will auto-transition to Employed when position assigned
        personnel.SetStatus(request.Status);

        _context.Personnel.Add(personnel);
        await _context.SaveChangesAsync(cancellationToken);

        return personnel.Id;
    }
}