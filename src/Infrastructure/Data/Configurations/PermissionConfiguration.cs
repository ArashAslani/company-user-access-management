using CompanyAccessManagement.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyAccessManagement.Infrastructure.Data.Configurations;

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions", "auth");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.ResourceId).IsRequired();
        builder.Property(p => p.ActionCode).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Description).HasMaxLength(500);

        builder.HasIndex(p => new { p.ResourceId, p.ActionCode }).IsUnique();

        builder.HasOne(p => p.Resource)
            .WithMany(r => r.Permissions)
            .HasForeignKey(p => p.ResourceId)
            .OnDelete(DeleteBehavior.Cascade);

builder.HasMany(p => p.AccessRules)
            .WithOne(ar => ar.Permission)
            .HasForeignKey(ar => ar.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Implications)
            .WithOne()
            .HasForeignKey(i => i.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
