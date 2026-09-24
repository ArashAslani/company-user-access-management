using CompanyAccessManagement.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyAccessManagement.Infrastructure.Data.Configurations;

public class UserCompanyConfiguration : IEntityTypeConfiguration<UserCompany>
{
    public void Configure(EntityTypeBuilder<UserCompany> builder)
    {
        builder.ToTable("UserCompanies", "auth");

        builder.HasKey(uc => uc.Id);

        builder.Property(uc => uc.UserId).IsRequired();
        builder.Property(uc => uc.CompanyId).IsRequired();
        builder.Property(uc => uc.PrincipalId).IsRequired();
        builder.Property(uc => uc.Status).IsRequired().HasConversion<int>();
        builder.Property(uc => uc.AuthorizationRevision).IsRequired();

        builder.HasIndex(uc => new { uc.UserId, uc.CompanyId }).IsUnique();
        builder.HasIndex(uc => uc.PrincipalId).IsUnique();

        builder.HasMany(uc => uc.Roles)
            .WithOne()
            .HasForeignKey(ur => ur.UserCompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
