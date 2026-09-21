using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.Organization.Personnel.Commands;

public record DeletePersonnelCommand : IRequest
{
    public Guid Id { get; init; }
}

public class DeletePersonnelCommandHandler : IRequestHandler<DeletePersonnelCommand>
{
    private readonly IApplicationDbContext _context;

    public DeletePersonnelCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(DeletePersonnelCommand request, CancellationToken cancellationToken)
    {
        var personnel = await _context.Personnel
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (personnel == null)
            throw new InvalidOperationException("Personnel not found.");

        if (!personnel.CanDelete())
            throw new InvalidOperationException("EMPLOYED_HAS_ACTIVE_POSITION");

        _context.Personnel.Remove(personnel);
        await _context.SaveChangesAsync(cancellationToken);
    }
}