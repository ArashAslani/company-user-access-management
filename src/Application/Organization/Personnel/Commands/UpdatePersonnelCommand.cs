using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Application.Common.Validation;
using CompanyAccessManagement.Domain.Common;
using CompanyAccessManagement.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Personnel.Commands;

public record UpdatePersonnelCommand : IRequest, IExternalIdentityFields
{
    public Guid Id { get; set; }
    public string FirstName { get; init; } = null!;
    public string LastName { get; init; } = null!;
    public string NationalCode { get; init; } = null!;
    public Gender Gender { get; init; }
    public string? PhoneNumber { get; init; }
    /// <summary>Omit to keep the current status. Employed cannot be set directly.</summary>
    public PersonnelStatus? Status { get; init; }
    public string? ExternalSource { get; init; }
    public string? ExternalId { get; init; }
}

public class UpdatePersonnelCommandHandler : IRequestHandler<UpdatePersonnelCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditWriter _audit;

    public UpdatePersonnelCommandHandler(IApplicationDbContext context, ICurrentWorkspace workspace, TimeProvider timeProvider, IAuditWriter audit)
    {
        _context = context;
        _workspace = workspace;
        _timeProvider = timeProvider;
        _audit = audit;
    }

    public async Task Handle(UpdatePersonnelCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var personnel = await _context.Personnel
            .VisibleIn(companyId)
            .Include(p => p.Positions)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        Guard.Against.NotFound(request.Id, personnel);
        var oldStatus = personnel.Status;

        var exists = await _context.Personnel
            .AnyAsync(p => p.CompanyId == companyId && p.NationalCode == request.NationalCode && p.Id != request.Id, cancellationToken);

        if (exists)
            throw new DomainRuleViolationException("DUPLICATE_NATIONAL_CODE", "National code is already registered in this company.");

        var (externalSource, externalId) = ExternalIdentity.Normalize(request.ExternalSource, request.ExternalId);
        if (externalSource is not null
            && await _context.Personnel.AnyAsync(p => p.CompanyId == companyId && p.Id != personnel.Id && p.ExternalSource == externalSource && p.ExternalId == externalId, cancellationToken))
            throw ExternalIdentityRules.Duplicate("personnel");

        personnel.UpdateDetails(request.FirstName, request.LastName, request.PhoneNumber, request.Gender);
        personnel.ChangeNationalCode(request.NationalCode);
        personnel.SetExternalIdentity(externalSource, externalId);
        if (request.Status is PersonnelStatus status)
            personnel.ChangeStatus(status, _timeProvider.GetUtcNow().UtcDateTime);

        _audit.Write(new AuditWriteRequest(AuditEventTypes.PersonnelUpdated, nameof(CompanyAccessManagement.Domain.Organization.Personnel), personnel.Id, CompanyId: companyId));
        if (oldStatus != personnel.Status)
            _audit.Write(new AuditWriteRequest(AuditEventTypes.PersonnelStatusChanged, nameof(CompanyAccessManagement.Domain.Organization.Personnel), personnel.Id, CompanyId: companyId));
        await _context.SaveChangesAsync(cancellationToken);
    }
}
