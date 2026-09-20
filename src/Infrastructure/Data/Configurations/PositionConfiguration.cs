using CleanArchitecture.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations;

public class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("Positions", "org");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.CompanyId).IsRequired();
        builder.Property(p => p.Code).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Title).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.Status).IsRequired().HasConversion<int>();

        builder.HasIndex(p => new { p.CompanyId, p.Code }).IsUnique();
        builder.HasIndex(p => new { p.CompanyId, p.ParentPositionId, p.Status });
        builder.HasIndex(p => new { p.CompanyId, p.Title });

        builder.HasOne(p => p.ParentPosition)
            .WithMany(p => p.Children)
            .HasForeignKey(p => p.ParentPositionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Assignments)
            .WithOne()
            .HasForeignKey(pp => pp.PositionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}