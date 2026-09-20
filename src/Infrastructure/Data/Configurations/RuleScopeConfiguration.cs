using CleanArchitecture.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations;

public class RuleScopeConfiguration : IEntityTypeConfiguration<RuleScope>
{
    public void Configure(EntityTypeBuilder<RuleScope> builder)
    {
        builder.ToTable("RuleScopes", "auth");

        builder.HasKey(rs => rs.Id);

        builder.Property(rs => rs.AccessRuleId).IsRequired();
        builder.Property(rs => rs.ScopeType).IsRequired().HasMaxLength(50);
        builder.Property(rs => rs.ScopeKey).IsRequired().HasMaxLength(100);

        builder.HasIndex(rs => new { rs.AccessRuleId, rs.ScopeType, rs.ScopeKey }).IsUnique();
        builder.HasIndex(rs => new { rs.ScopeType, rs.ScopeKey, rs.AccessRuleId });

        builder.HasOne(rs => rs.AccessRule)
            .WithMany(ar => ar.Scopes)
            .HasForeignKey(rs => rs.AccessRuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}