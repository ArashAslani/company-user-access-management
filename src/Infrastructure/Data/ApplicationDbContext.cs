using System.Reflection;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.AccessControl;
using CleanArchitecture.Domain.Organization;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    // Organization
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Personnel> Personnel => Set<Personnel>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<PersonnelPosition> PersonnelPositions => Set<PersonnelPosition>();
    public DbSet<PersonnelSignature> PersonnelSignatures => Set<PersonnelSignature>();

    // AccessControl / Authorization
    public DbSet<UserCompany> UserCompanies => Set<UserCompany>();
    public DbSet<Role> BusinessRoles => Set<Role>();
    public new DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<AuthPrincipal> AuthPrincipals => Set<AuthPrincipal>();
    public DbSet<CleanArchitecture.Domain.AccessControl.Application> Applications => Set<CleanArchitecture.Domain.AccessControl.Application>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<PermissionImplication> PermissionImplications => Set<PermissionImplication>();
    public DbSet<AccessRule> AccessRules => Set<AccessRule>();
    public DbSet<RuleScope> RuleScopes => Set<RuleScope>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Explicit interface implementation for IApplicationDbContext
    DbSet<Role> IApplicationDbContext.Roles => BusinessRoles;
    DbSet<CleanArchitecture.Domain.AccessControl.Application> IApplicationDbContext.Applications => Applications;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Identity tables in 'identity' schema
        builder.Entity<ApplicationUser>(b => b.ToTable("AspNetUsers", "identity"));
        builder.Entity<IdentityRole<Guid>>(b => b.ToTable("AspNetRoles", "identity"));
        builder.Entity<IdentityUserRole<Guid>>(b => b.ToTable("AspNetUserRoles", "identity"));
        builder.Entity<IdentityUserClaim<Guid>>(b => b.ToTable("AspNetUserClaims", "identity"));
        builder.Entity<IdentityUserLogin<Guid>>(b => b.ToTable("AspNetUserLogins", "identity"));
        builder.Entity<IdentityRoleClaim<Guid>>(b => b.ToTable("AspNetRoleClaims", "identity"));
        builder.Entity<IdentityUserToken<Guid>>(b => b.ToTable("AspNetUserTokens", "identity"));
    }
}