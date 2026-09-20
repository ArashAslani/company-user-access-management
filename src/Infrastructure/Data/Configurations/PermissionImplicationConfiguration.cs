using CleanArchitecture.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations;

public class PermissionImplicationConfiguration : IEntityTypeConfiguration<PermissionImplication>
{
    public void Configure(EntityTypeBuilder<PermissionImplication> builder)
    {
        builder.ToTable("PermissionImplications", "auth");

        builder.HasKey(i => new { i.PermissionId, i.RequiredPermissionId });

        builder.HasOne(i => i.Permission)
            .WithMany(p => p.Implications)
            .HasForeignKey(i => i.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.RequiredPermission)
            .WithMany()
            .HasForeignKey(i => i.RequiredPermissionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}