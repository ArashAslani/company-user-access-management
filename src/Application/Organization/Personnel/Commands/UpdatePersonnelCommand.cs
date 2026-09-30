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
    public PersonnelStatus Status { get; init; }
}

public class UpdatePersonnelCommandHandler : IRequestHandler<UpdatePersonnelCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;

    public UpdatePersonnelCommandHandler(IApplicationDbContext context, ICurrentWorkspace workspace)
    {
        _context = context;
        _workspace = workspace;
    }

    public async Task Handle(UpdatePersonnelCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var personnel = await _context.Personnel
            .VisibleIn(_context, companyId)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        Guard.Against.NotFound(request.Id, personnel);

        // Check duplicate national code (excluding self)
        var exists = await _context.Personnel
            .AnyAsync(p => p.NationalCode == request.NationalCode && p.Id != request.Id, cancellationToken);

        if (exists)
            throw new DomainRuleViolationException("DUPLICATE_NATIONAL_CODE", "National code is already registered.");

        personnel.UpdateDetails(request.FirstName, request.LastName, request.PhoneNumber, request.Gender);
        personnel.SetStatus(request.Status);

        await _context.SaveChangesAsync(cancellationToken);
    }
}
