using CleanArchitecture.Domain.AccessControl;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.Organization;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    // Existing
    DbSet<TodoList> TodoLists { get; }
    DbSet<TodoItem> TodoItems { get; }

    // Organization
    DbSet<Personnel> Personnel { get; }
    DbSet<Position> Positions { get; }
    DbSet<PersonnelPosition> PersonnelPositions { get; }
    DbSet<PersonnelSignature> PersonnelSignatures { get; }

    // AccessControl / Authorization
    DbSet<UserCompany> UserCompanies { get; }
    DbSet<Role> Roles { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RoleGroup> RoleGroups { get; }
    DbSet<RoleGroupRole> RoleGroupRoles { get; }
    DbSet<AuthPrincipal> AuthPrincipals { get; }
    DbSet<CleanArchitecture.Domain.AccessControl.Application> Applications { get; }
    DbSet<Resource> Resources { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<PermissionImplication> PermissionImplications { get; }
    DbSet<AccessRule> AccessRules { get; }
    DbSet<RuleScope> RuleScopes { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}