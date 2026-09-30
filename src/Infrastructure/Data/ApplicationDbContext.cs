using System.Reflection;
using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Common;
using CompanyAccessManagement.Domain.Organization;
using CompanyAccessManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Infrastructure.Data;

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
    public DbSet<CompanyAccessManagement.Domain.AccessControl.Application> Applications => Set<CompanyAccessManagement.Domain.AccessControl.Application>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<PermissionImplication> PermissionImplications => Set<PermissionImplication>();
    public DbSet<AccessRule> AccessRules => Set<AccessRule>();
    public DbSet<RuleScope> RuleScopes => Set<RuleScope>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Explicit interface implementation for IApplicationDbContext
    DbSet<Role> IApplicationDbContext.Roles => BusinessRoles;
    DbSet<CompanyAccessManagement.Domain.AccessControl.Application> IApplicationDbContext.Applications => Applications;

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (IsExternalIdentityUniqueViolation(ex))
        {
            // A concurrent writer won the race past the handler's pre-check; the filtered unique index is the backstop.
            throw new DomainRuleViolationException("EXTERNAL_IDENTITY_DUPLICATE", "Another record already uses this external identity.");
        }
    }

    private const int SqliteConstraintUnique = 2067;

    private static bool IsExternalIdentityUniqueViolation(DbUpdateException ex)
        => ex.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteConstraintUnique } sqlite
            && sqlite.Message.Contains(".ExternalSource", StringComparison.Ordinal)
            && sqlite.Message.Contains(".ExternalId", StringComparison.Ordinal);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Identity tables in 'identity' schema
        builder.Entity<ApplicationUser>(b =>
        {
            b.ToTable("AspNetUsers", "identity");
            // At most one live operational account per personnel; deleted accounts keep their link as history.
            b.HasIndex(u => u.PersonnelId)
                .IsUnique()
                .HasFilter("\"PersonnelId\" IS NOT NULL AND \"IsDeleted\" = 0");
        });
        builder.Entity<IdentityRole<Guid>>(b => b.ToTable("AspNetRoles", "identity"));
        builder.Entity<IdentityUserRole<Guid>>(b => b.ToTable("AspNetUserRoles", "identity"));
        builder.Entity<IdentityUserClaim<Guid>>(b => b.ToTable("AspNetUserClaims", "identity"));
        builder.Entity<IdentityUserLogin<Guid>>(b => b.ToTable("AspNetUserLogins", "identity"));
        builder.Entity<IdentityRoleClaim<Guid>>(b => b.ToTable("AspNetRoleClaims", "identity"));
        builder.Entity<IdentityUserToken<Guid>>(b => b.ToTable("AspNetUserTokens", "identity"));
    }
}
