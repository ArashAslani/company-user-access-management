using CompanyAccessManagement.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyAccessManagement.Infrastructure.Data.Configurations;

public class PersonnelPositionConfiguration : IEntityTypeConfiguration<PersonnelPosition>
{
    public void Configure(EntityTypeBuilder<PersonnelPosition> builder)
    {
        builder.ToTable("PersonnelPositions", "org");

        builder.HasKey(pp => new { pp.PersonnelId, pp.PositionId });

        builder.Property(pp => pp.IsPrimary).IsRequired();
        builder.Property(pp => pp.Status).IsRequired().HasConversion<int>();
        builder.Property(pp => pp.EffectiveFrom).IsRequired();
        builder.Property(pp => pp.EffectiveTo);
        builder.Property(pp => pp.CreatedAt).IsRequired();
        builder.Property(pp => pp.DeactivatedAt);

        builder.HasIndex(pp => new { pp.PersonnelId, pp.Status });
        builder.HasIndex(pp => new { pp.PositionId, pp.Status });
        builder.HasIndex(pp => new { pp.PersonnelId, pp.EffectiveFrom, pp.EffectiveTo });

        builder.HasOne(pp => pp.Personnel)
            .WithMany(p => p.Positions)
            .HasForeignKey(pp => pp.PersonnelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pp => pp.Position)
            .WithMany(p => p.Assignments)
            .HasForeignKey(pp => pp.PositionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
