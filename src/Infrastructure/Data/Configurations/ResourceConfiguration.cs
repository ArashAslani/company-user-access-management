using CompanyAccessManagement.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyAccessManagement.Infrastructure.Data.Configurations;

public class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> builder)
    {
        builder.ToTable("Resources", "auth");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ApplicationId).IsRequired();
        builder.Property(r => r.Code).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.SortOrder).IsRequired();

        builder.HasIndex(r => new { r.ApplicationId, r.Code }).IsUnique();
        builder.HasIndex(r => r.ParentResourceId);

        builder.HasOne(r => r.Application)
            .WithMany(a => a.Resources)
            .HasForeignKey(r => r.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.ParentResource)
            .WithMany(r => r.Children)
            .HasForeignKey(r => r.ParentResourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Permissions)
            .WithOne()
            .HasForeignKey(p => p.ResourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
