using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Organization;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    // Organization
    DbSet<Company> Companies { get; }
    DbSet<Personnel> Personnel { get; }
    DbSet<Position> Positions { get; }
    DbSet<PersonnelPosition> PersonnelPositions { get; }
    DbSet<PersonnelSignature> PersonnelSignatures { get; }

    // AccessControl / Authorization
    DbSet<UserCompany> UserCompanies { get; }
    DbSet<Role> Roles { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<AuthPrincipal> AuthPrincipals { get; }
    DbSet<CompanyAccessManagement.Domain.AccessControl.Application> Applications { get; }
    DbSet<Resource> Resources { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<PermissionImplication> PermissionImplications { get; }
    DbSet<AccessRule> AccessRules { get; }
    DbSet<RuleScope> RuleScopes { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
