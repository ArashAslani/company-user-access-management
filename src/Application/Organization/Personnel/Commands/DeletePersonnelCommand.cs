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
    private readonly TimeProvider _timeProvider;

    public DeletePersonnelCommandHandler(IApplicationDbContext context, ICurrentWorkspace workspace, TimeProvider timeProvider)
    {
        _context = context;
        _workspace = workspace;
        _timeProvider = timeProvider;
    }

    public async Task Handle(DeletePersonnelCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var personnel = await _context.Personnel
            .VisibleIn(_context, companyId)
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        Guard.Against.NotFound(request.Id, personnel);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!personnel.CanDelete(now))
            throw new DomainRuleViolationException("EMPLOYED_HAS_ACTIVE_POSITION", "Personnel with an active position cannot be deleted.");

        personnel.Deactivate(now);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
