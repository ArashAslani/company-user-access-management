using CompanyAccessManagement.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyAccessManagement.Infrastructure.Data.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles", "auth");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.ApplicationId).IsRequired();
        builder.Property(r => r.PrincipalId).IsRequired();
        builder.Property(r => r.Name).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Kind).IsRequired().HasConversion<int>();
        builder.Property(r => r.ValidUntil);
        builder.Property(r => r.Status).IsRequired().HasConversion<int>();

        builder.HasIndex(r => new { r.CompanyId, r.ApplicationId, r.ParentRoleId });
        builder.HasIndex(r => r.PrincipalId).IsUnique();

        builder.HasOne(r => r.ParentRole)
            .WithMany(r => r.Children)
            .HasForeignKey(r => r.ParentRoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.UserRoles)
            .WithOne()
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

builder.HasMany(r => r.AccessRules)
            .WithOne(ar => ar.AuthorityRole)
            .HasForeignKey(ar => ar.AuthorityRoleId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
