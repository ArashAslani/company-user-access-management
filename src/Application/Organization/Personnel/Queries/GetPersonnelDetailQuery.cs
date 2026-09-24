using CompanyAccessManagement.Application.Common.Interfaces;
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

    public GetPersonnelDetailQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PersonnelDetailDto?> Handle(GetPersonnelDetailQuery request, CancellationToken cancellationToken)
    {
        var personnel = await _context.Personnel
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
            CompanyId = personnel.Positions.FirstOrDefault(pp => pp.Position != null && pp.IsCurrentlyEffective())?.Position?.CompanyId ?? Guid.Empty,
            Status = personnel.Status,
            Positions = personnel.Positions
                .Where(p => p.IsCurrentlyEffective())
                .Select(p => new PersonnelPositionDto
                {
                    PersonnelPositionId = p.PersonnelId,
                    PositionId = p.PositionId,
                    PositionCode = p.Position?.Code ?? string.Empty,
                    PositionTitle = p.Position?.Title ?? string.Empty,
                    IsPrimary = p.IsPrimary,
                    EffectiveFrom = p.EffectiveFrom,
                    EffectiveTo = p.EffectiveTo,
                    Status = p.Status,
                    AccessGroupHint = "Role-based access group hint"
                }).ToList(),
            Attachments = new List<AttachmentDto>(),
            Signature = currentSig != null ? new SignatureDto
            {
                Id = currentSig.Id,
                Version = currentSig.Version,
                Url = $"/api/v1/attachments/{currentSig.Id}",
                IsCurrent = currentSig.IsCurrent
            } : null
        };
    }
}
