using CompanyAccessManagement.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyAccessManagement.Infrastructure.Data.Configurations;

public class AccessRuleConfiguration : IEntityTypeConfiguration<AccessRule>
{
    public void Configure(EntityTypeBuilder<AccessRule> builder)
    {
        builder.ToTable("AccessRules", "auth", t => t.HasCheckConstraint("CK_AccessRules_ScopeMode", "[ScopeMode] IN (0, 1, 2)"));

        builder.HasKey(ar => ar.Id);

        builder.Property(ar => ar.PrincipalId).IsRequired();
        builder.Property(ar => ar.PermissionId).IsRequired();
        builder.Property(ar => ar.AuthorityRoleId);
        builder.Property(ar => ar.DelegatedFromUserId);
        builder.Property(ar => ar.Effect).IsRequired().HasConversion<int>();
        builder.Property(ar => ar.Origin).IsRequired().HasConversion<int>();
        builder.Property(ar => ar.ScopeMode).IsRequired().HasConversion<int>();
        builder.Property(ar => ar.ValidFrom);
        builder.Property(ar => ar.ValidUntil);
        builder.Property(ar => ar.Status).IsRequired().HasConversion<int>();

        builder.HasIndex(ar => new { ar.PrincipalId, ar.PermissionId, ar.Status });
        builder.HasIndex(ar => new { ar.PermissionId, ar.Effect, ar.Status });
        builder.HasIndex(ar => ar.AuthorityRoleId);

        builder.HasOne(ar => ar.Principal)
            .WithMany(p => p.AccessRules)
            .HasForeignKey(ar => ar.PrincipalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ar => ar.Permission)
            .WithMany(p => p.AccessRules)
            .HasForeignKey(ar => ar.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ar => ar.AuthorityRole)
            .WithMany(r => r.AccessRules)
            .HasForeignKey(ar => ar.AuthorityRoleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(ar => ar.Scopes)
            .WithOne()
            .HasForeignKey(rs => rs.AccessRuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
