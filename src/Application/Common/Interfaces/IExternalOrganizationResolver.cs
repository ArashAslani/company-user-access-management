namespace CompanyAccessManagement.Application.Common.Interfaces;

/// <summary>
/// Maps an ERP/HR identity to the internal id of an organization record. Sources are matched after the same
/// normalization used on write (trimmed, upper-case); ids are matched exactly. Returns null when unknown.
/// Company identities are global; the others are unique only within their owner.
/// </summary>
public interface IExternalOrganizationResolver
{
    Task<Guid?> ResolveCompanyAsync(string externalSource, string externalId, CancellationToken cancellationToken = default);
    Task<Guid?> ResolvePositionAsync(Guid companyId, string externalSource, string externalId, CancellationToken cancellationToken = default);
    Task<Guid?> ResolvePersonnelAsync(Guid companyId, string externalSource, string externalId, CancellationToken cancellationToken = default);
    Task<Guid?> ResolvePersonnelPositionAsync(Guid personnelId, string externalSource, string externalId, CancellationToken cancellationToken = default);
}
