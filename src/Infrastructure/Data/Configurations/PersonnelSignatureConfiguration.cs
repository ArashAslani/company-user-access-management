using CleanArchitecture.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations;

public class PersonnelSignatureConfiguration : IEntityTypeConfiguration<PersonnelSignature>
{
    public void Configure(EntityTypeBuilder<PersonnelSignature> builder)
    {
        builder.ToTable("PersonnelSignatures", "org", t => t.HasCheckConstraint("CK_PersonnelSignatures_SizeBytes", "[SizeBytes] <= 8388608"));

        builder.HasKey(s => s.Id);

        builder.Property(s => s.PersonnelId).IsRequired();
        builder.Property(s => s.Version).IsRequired();
        builder.Property(s => s.MimeType).IsRequired().HasMaxLength(50);
        builder.Property(s => s.SizeBytes).IsRequired();
        builder.Property(s => s.ContentHash).IsRequired().HasMaxLength(64);
        builder.Property(s => s.Content).IsRequired();
        builder.Property(s => s.IsCurrent).IsRequired();
        builder.Property(s => s.CreatedByUserId);

        builder.HasIndex(s => new { s.PersonnelId, s.Version }).IsUnique();
        builder.HasIndex(s => new { s.PersonnelId, s.IsCurrent })
            .IsUnique()
            .HasFilter("[IsCurrent] = 1");

        builder.HasOne(s => s.Personnel)
            .WithMany(p => p.Signatures)
            .HasForeignKey(s => s.PersonnelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}