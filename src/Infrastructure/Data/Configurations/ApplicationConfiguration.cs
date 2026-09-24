using CompanyAccessManagement.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyAccessManagement.Infrastructure.Data.Configurations;

public class ApplicationConfiguration : IEntityTypeConfiguration<CompanyAccessManagement.Domain.AccessControl.Application>
{
    public void Configure(EntityTypeBuilder<CompanyAccessManagement.Domain.AccessControl.Application> builder)
    {
        builder.ToTable("Applications", "auth");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Code).IsRequired().HasMaxLength(50);
        builder.Property(a => a.Name).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Description).HasMaxLength(500);
        builder.Property(a => a.IsActive).IsRequired();
        builder.Property(a => a.PolicyRevision).IsRequired();

        builder.HasIndex(a => a.Code).IsUnique();

        builder.HasMany(a => a.Resources)
            .WithOne()
            .HasForeignKey(r => r.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.Roles)
            .WithOne()
            .HasForeignKey(r => r.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
