using CompanyAccessManagement.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyAccessManagement.Infrastructure.Data.Configurations;

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments", "org");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.OwnerType).IsRequired().HasConversion<int>();
        builder.Property(a => a.OwnerId).IsRequired();
        builder.Property(a => a.CompanyId).IsRequired();
        builder.Property(a => a.FileName).IsRequired().HasMaxLength(260);
        builder.Property(a => a.MimeType).IsRequired().HasMaxLength(150);
        builder.Property(a => a.SizeBytes).IsRequired();
        builder.Property(a => a.ContentHash).IsRequired().HasMaxLength(128);
        builder.Property(a => a.Content).IsRequired();
        builder.Property(a => a.UploadedAt).IsRequired();

        builder.HasIndex(a => new { a.OwnerType, a.OwnerId });
        builder.HasIndex(a => a.CompanyId);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(a => a.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
