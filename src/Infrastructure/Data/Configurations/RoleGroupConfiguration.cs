using CleanArchitecture.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations;

public class RoleGroupConfiguration : IEntityTypeConfiguration<RoleGroup>
{
    public void Configure(EntityTypeBuilder<RoleGroup> builder)
    {
        builder.ToTable("RoleGroups", "auth");

        builder.HasKey(rg => rg.Id);

        builder.Property(rg => rg.CompanyId).IsRequired();
        builder.Property(rg => rg.ApplicationId).IsRequired();
        builder.Property(rg => rg.PrincipalId).IsRequired();
        builder.Property(rg => rg.Name).IsRequired().HasMaxLength(100);
        builder.Property(rg => rg.Description).HasMaxLength(500);
        builder.Property(rg => rg.Status).IsRequired().HasConversion<int>();

        builder.HasIndex(rg => rg.PrincipalId).IsUnique();

        builder.HasMany(rg => rg.RoleGroupRoles)
            .WithOne()
            .HasForeignKey(rgr => rgr.RoleGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(rg => rg.AccessRules)
            .WithOne()
            .HasForeignKey(ar => ar.PrincipalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}