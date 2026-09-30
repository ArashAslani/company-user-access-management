using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Infrastructure.Organization;

public sealed class ExternalOrganizationResolver : IExternalOrganizationResolver
{
    private readonly IApplicationDbContext _context;

    public ExternalOrganizationResolver(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Guid?> ResolveCompanyAsync(string externalSource, string externalId, CancellationToken cancellationToken = default)
    {
        var source = ExternalIdentity.NormalizeSource(externalSource);
        return await _context.Companies.AsNoTracking()
            .Where(c => c.ExternalSource == source && c.ExternalId == externalId)
            .Select(c => (Guid?)c.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid?> ResolvePositionAsync(Guid companyId, string externalSource, string externalId, CancellationToken cancellationToken = default)
    {
        var source = ExternalIdentity.NormalizeSource(externalSource);
        return await _context.Positions.AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.ExternalSource == source && p.ExternalId == externalId)
            .Select(p => (Guid?)p.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid?> ResolvePersonnelAsync(Guid companyId, string externalSource, string externalId, CancellationToken cancellationToken = default)
    {
        var source = ExternalIdentity.NormalizeSource(externalSource);
        return await _context.Personnel.AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.ExternalSource == source && p.ExternalId == externalId)
            .Select(p => (Guid?)p.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid?> ResolvePersonnelPositionAsync(Guid personnelId, string externalSource, string externalId, CancellationToken cancellationToken = default)
    {
        var source = ExternalIdentity.NormalizeSource(externalSource);
        return await _context.PersonnelPositions.AsNoTracking()
            .Where(pp => pp.PersonnelId == personnelId && pp.ExternalSource == source && pp.ExternalId == externalId)
            .Select(pp => (Guid?)pp.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
