using CompanyAccessManagement.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyAccessManagement.Infrastructure.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs", "auth");

        builder.HasKey(al => al.Id);

        builder.Property(al => al.CompanyId);
        builder.Property(al => al.ApplicationId);
        builder.Property(al => al.ActorUserId).IsRequired();
        builder.Property(al => al.OperationId).IsRequired();
        builder.Property(al => al.EntityType).IsRequired().HasMaxLength(100);
        builder.Property(al => al.EntityId).IsRequired();
        builder.Property(al => al.TargetPrincipalId);
        builder.Property(al => al.PermissionId);
        builder.Property(al => al.SourceType).IsRequired().HasConversion<int>();
        builder.Property(al => al.EventType).IsRequired().HasMaxLength(100);
        builder.Property(al => al.BeforeData);
        builder.Property(al => al.AfterData);
        builder.Property(al => al.Metadata);
        builder.Property(al => al.Created).IsRequired();

        builder.HasIndex(al => new { al.CompanyId, al.Created });
        builder.HasIndex(al => new { al.ActorUserId, al.Created });
        builder.HasIndex(al => new { al.EntityType, al.EntityId, al.Created });
        builder.HasIndex(al => al.OperationId);
    }
}
