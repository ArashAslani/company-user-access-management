using CleanArchitecture.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations;

public class RoleGroupRoleConfiguration : IEntityTypeConfiguration<RoleGroupRole>
{
    public void Configure(EntityTypeBuilder<RoleGroupRole> builder)
    {
        builder.ToTable("RoleGroupRoles", "auth");

        builder.HasKey(rgr => new { rgr.RoleGroupId, rgr.RoleId });

        builder.HasOne(rgr => rgr.RoleGroup)
            .WithMany(rg => rg.RoleGroupRoles)
            .HasForeignKey(rgr => rgr.RoleGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rgr => rgr.Role)
            .WithMany(r => r.RoleGroupRoles)
            .HasForeignKey(rgr => rgr.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}