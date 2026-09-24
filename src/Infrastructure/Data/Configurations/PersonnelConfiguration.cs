using CompanyAccessManagement.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyAccessManagement.Infrastructure.Data.Configurations;

public class PersonnelConfiguration : IEntityTypeConfiguration<Personnel>
{
    public void Configure(EntityTypeBuilder<Personnel> builder)
    {
        builder.ToTable("Personnel", "org");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.NationalCode)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(p => p.PersonnelCode)
            .HasMaxLength(50);

        builder.Property(p => p.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.PhoneNumber)
            .HasMaxLength(20);

        builder.Property(p => p.Gender)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(p => p.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.HasIndex(p => p.NationalCode).IsUnique();
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => new { p.LastName, p.FirstName });

        builder.HasMany(p => p.Positions)
            .WithOne()
            .HasForeignKey(pp => pp.PersonnelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Signatures)
            .WithOne()
            .HasForeignKey(s => s.PersonnelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
