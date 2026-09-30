using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.Common;
using CompanyAccessManagement.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Personnel.Commands;

public record DeletePersonnelCommand : IRequest
{
    public Guid Id { get; init; }
}

public class DeletePersonnelCommandHandler : IRequestHandler<DeletePersonnelCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;

    public DeletePersonnelCommandHandler(IApplicationDbContext context, ICurrentWorkspace workspace)
    {
        _context = context;
        _workspace = workspace;
    }

    public async Task Handle(DeletePersonnelCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var personnel = await _context.Personnel
            .VisibleIn(_context, companyId)
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        Guard.Against.NotFound(request.Id, personnel);

        if (!personnel.CanDelete())
            throw new DomainRuleViolationException("EMPLOYED_HAS_ACTIVE_POSITION", "Personnel with an active position cannot be deleted.");

        _context.Personnel.Remove(personnel);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
