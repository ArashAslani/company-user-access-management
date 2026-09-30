using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Application.Common.Validation;
using CompanyAccessManagement.Domain.Common;
using CompanyAccessManagement.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Personnel.Commands;

public record CreatePersonnelCommand : IRequest<Guid>, IExternalIdentityFields
{
    public string FirstName { get; init; } = null!;
    public string LastName { get; init; } = null!;
    public string NationalCode { get; init; } = null!;
    public Gender Gender { get; init; }
    public string? PhoneNumber { get; init; }
    public Guid CompanyId { get; init; }
    public PersonnelStatus Status { get; init; } = PersonnelStatus.Draft;
    public string? ExternalSource { get; init; }
    public string? ExternalId { get; init; }
}

public class CreatePersonnelCommandHandler : IRequestHandler<CreatePersonnelCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;
    private readonly TimeProvider _timeProvider;

    public CreatePersonnelCommandHandler(IApplicationDbContext context, ICurrentWorkspace workspace, TimeProvider timeProvider)
    {
        _context = context;
        _workspace = workspace;
        _timeProvider = timeProvider;
    }

    public async Task<Guid> Handle(CreatePersonnelCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.EnsureCompany(request.CompanyId);

        var exists = await _context.Personnel
            .AnyAsync(p => p.CompanyId == companyId && p.NationalCode == request.NationalCode, cancellationToken);

        if (exists)
            throw new DomainRuleViolationException("DUPLICATE_NATIONAL_CODE", "National code is already registered in this company.");

        var (externalSource, externalId) = ExternalIdentity.Normalize(request.ExternalSource, request.ExternalId);
        if (externalSource is not null
            && await _context.Personnel.AnyAsync(p => p.CompanyId == companyId && p.ExternalSource == externalSource && p.ExternalId == externalId, cancellationToken))
            throw ExternalIdentityRules.Duplicate("personnel");

        var personnel = new CompanyAccessManagement.Domain.Organization.Personnel(
            companyId,
            request.NationalCode, 
            request.FirstName, 
            request.LastName, 
            request.Gender, 
            null, 
            request.PhoneNumber);
        personnel.SetExternalIdentity(externalSource, externalId);

        // Created as Draft; employment follows an effective position assignment.
        personnel.ChangeStatus(request.Status, _timeProvider.GetUtcNow().UtcDateTime);

        _context.Personnel.Add(personnel);
        await _context.SaveChangesAsync(cancellationToken);

        return personnel.Id;
    }
}
