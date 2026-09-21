using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.Organization.Personnel.Commands;

public record UpdatePersonnelCommand : IRequest
{
    public Guid Id { get; set; }
    public string FirstName { get; init; } = null!;
    public string LastName { get; init; } = null!;
    public string NationalCode { get; init; } = null!;
    public Gender Gender { get; init; }
    public string? PhoneNumber { get; init; }
    public PersonnelStatus Status { get; init; }
}

public class UpdatePersonnelCommandHandler : IRequestHandler<UpdatePersonnelCommand>
{
    private readonly IApplicationDbContext _context;

    public UpdatePersonnelCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(UpdatePersonnelCommand request, CancellationToken cancellationToken)
    {
        var personnel = await _context.Personnel
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (personnel == null)
            throw new InvalidOperationException("Personnel not found.");

        // Check duplicate national code (excluding self)
        var exists = await _context.Personnel
            .AnyAsync(p => p.NationalCode == request.NationalCode && p.Id != request.Id, cancellationToken);

        if (exists)
            throw new InvalidOperationException("DUPLICATE_NATIONAL_CODE");

        personnel.UpdateDetails(request.FirstName, request.LastName, request.PhoneNumber, request.Gender);
        personnel.SetStatus(request.Status);

        await _context.SaveChangesAsync(cancellationToken);
    }
}