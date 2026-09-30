using CompanyAccessManagement.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyAccessManagement.Infrastructure.Data.Configurations;

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("Companies", "org", t => t.HasCheckConstraint(ExternalIdentityConfiguration.CheckConstraintName("Companies"), ExternalIdentityConfiguration.CheckConstraintSql));

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Code).IsRequired().HasMaxLength(50);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.Property(c => c.Status).IsRequired().HasConversion<int>();
        builder.Property(c => c.OrganizationRevision).IsRequired().IsConcurrencyToken();
        builder.Property(c => c.AuthorizationRevision).IsRequired().IsConcurrencyToken();

        builder.HasIndex(c => c.Code).IsUnique();
        builder.HasIndex(c => c.ParentCompanyId);
        builder.HasExternalIdentity(c => new { c.ExternalSource, c.ExternalId });

        builder.HasOne(c => c.ParentCompany)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentCompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
