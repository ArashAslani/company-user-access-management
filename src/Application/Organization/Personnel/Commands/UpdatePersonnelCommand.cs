using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.Common;
using CompanyAccessManagement.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Personnel.Commands;

public record UpdatePersonnelCommand : IRequest
{
    public Guid Id { get; set; }
    public string FirstName { get; init; } = null!;
    public string LastName { get; init; } = null!;
    public string NationalCode { get; init; } = null!;
    public Gender Gender { get; init; }
    public string? PhoneNumber { get; init; }
    /// <summary>Omit to keep the current status. Employed cannot be set directly.</summary>
    public PersonnelStatus? Status { get; init; }
}

public class UpdatePersonnelCommandHandler : IRequestHandler<UpdatePersonnelCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;
    private readonly TimeProvider _timeProvider;

    public UpdatePersonnelCommandHandler(IApplicationDbContext context, ICurrentWorkspace workspace, TimeProvider timeProvider)
    {
        _context = context;
        _workspace = workspace;
        _timeProvider = timeProvider;
    }

    public async Task Handle(UpdatePersonnelCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var personnel = await _context.Personnel
            .VisibleIn(_context, companyId)
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        Guard.Against.NotFound(request.Id, personnel);

        // Check duplicate national code (excluding self)
        var exists = await _context.Personnel
            .AnyAsync(p => p.NationalCode == request.NationalCode && p.Id != request.Id, cancellationToken);

        if (exists)
            throw new DomainRuleViolationException("DUPLICATE_NATIONAL_CODE", "National code is already registered.");

        personnel.UpdateDetails(request.FirstName, request.LastName, request.PhoneNumber, request.Gender);
        personnel.ChangeNationalCode(request.NationalCode);
        if (request.Status is PersonnelStatus status)
            personnel.ChangeStatus(status, _timeProvider.GetUtcNow().UtcDateTime);

        await _context.SaveChangesAsync(cancellationToken);
    }
}
