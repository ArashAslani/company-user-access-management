using CompanyAccessManagement.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyAccessManagement.Infrastructure.Data.Configurations;

public class AuthPrincipalConfiguration : IEntityTypeConfiguration<AuthPrincipal>
{
    public void Configure(EntityTypeBuilder<AuthPrincipal> builder)
    {
        builder.ToTable("AuthPrincipals", "auth");

        builder.HasKey(ap => ap.Id);

        builder.Property(ap => ap.Type).IsRequired().HasConversion<int>();
        builder.Property(ap => ap.ReferenceId).IsRequired();
        builder.Property(ap => ap.CompanyId).IsRequired();
        builder.Property(ap => ap.ApplicationId).IsRequired();

        builder.HasIndex(ap => new { ap.Type, ap.ReferenceId, ap.CompanyId, ap.ApplicationId }).IsUnique();
        builder.HasIndex(ap => new { ap.CompanyId, ap.ApplicationId });

builder.HasMany(ap => ap.AccessRules)
            .WithOne(ar => ar.Principal)
            .HasForeignKey(ar => ar.PrincipalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
