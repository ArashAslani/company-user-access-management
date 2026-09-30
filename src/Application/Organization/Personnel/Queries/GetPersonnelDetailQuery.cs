using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Application.Organization.Personnel.Queries;
using CompanyAccessManagement.Domain.Organization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Organization.Personnel.Queries;

public record GetPersonnelDetailQuery : IRequest<PersonnelDetailDto?>
{
    public Guid Id { get; init; }
}

public class GetPersonnelDetailQueryHandler : IRequestHandler<GetPersonnelDetailQuery, PersonnelDetailDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;

    public GetPersonnelDetailQueryHandler(IApplicationDbContext context, ICurrentWorkspace workspace)
    {
        _context = context;
        _workspace = workspace;
    }

    public async Task<PersonnelDetailDto?> Handle(GetPersonnelDetailQuery request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var personnel = await _context.Personnel
            .VisibleIn(companyId)
            .Include(p => p.Positions)
                .ThenInclude(pp => pp.Position)
            .Include(p => p.Signatures)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (personnel == null)
            return null;

        var currentSig = personnel.GetCurrentSignature();

        return new PersonnelDetailDto
        {
            Id = personnel.Id,
            FirstName = personnel.FirstName,
            LastName = personnel.LastName,
            NationalCode = personnel.NationalCode,
            Gender = personnel.Gender,
            PhoneNumber = personnel.PhoneNumber,
            CompanyId = personnel.CompanyId,
            ExternalSource = personnel.ExternalSource,
            ExternalId = personnel.ExternalId,
            Status = personnel.Status,
            Positions = personnel.Positions
                .Where(p => p.IsActive)
                .OrderBy(p => p.EffectiveFrom)
                .Select(p => new PersonnelPositionDto
                {
                    PersonnelPositionId = p.Id,
                    PositionId = p.PositionId,
                    PositionCode = p.Position?.Code ?? string.Empty,
                    PositionTitle = p.Position?.Title ?? string.Empty,
                    IsPrimary = p.IsPrimary,
                    EffectiveFrom = p.EffectiveFrom,
                    EffectiveTo = p.EffectiveTo,
                    Status = p.Status,
                    AccessGroupHint = "Role-based access group hint",
                    ExternalSource = p.ExternalSource,
                    ExternalId = p.ExternalId
                }).ToList(),
            Signature = currentSig != null ? new SignatureDto
            {
                Id = currentSig.Id,
                Version = currentSig.Version,
                IsCurrent = currentSig.IsCurrent
            } : null
        };
    }
}
