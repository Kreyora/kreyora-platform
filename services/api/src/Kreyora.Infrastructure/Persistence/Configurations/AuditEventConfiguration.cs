using Kreyora.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(26);
        builder.Property(item => item.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.ActorUserId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.EffectiveSupportActorUserId).HasMaxLength(26);
        builder.Property(item => item.SupportAccessGrantId).HasMaxLength(26);
        builder.Property(item => item.Action).IsRequired().HasMaxLength(AuditEvent.ActionMaxLength);
        builder.Property(item => item.TargetType).IsRequired().HasMaxLength(AuditEvent.TargetTypeMaxLength);
        builder.Property(item => item.TargetId).IsRequired().HasMaxLength(AuditEvent.TargetIdMaxLength);
        builder.Property(item => item.OccurredAt).IsRequired();
        builder.Property(item => item.Reason).HasMaxLength(AuditEvent.ReasonMaxLength);
        builder.Property(item => item.CorrelationId).IsRequired().HasMaxLength(AuditEvent.CorrelationIdMaxLength);
        builder.Property(item => item.Metadata).HasColumnType("jsonb");
        builder.Property(item => item.CreatedAt).IsRequired();
        builder.Property(item => item.ModifiedAt).IsRequired();
        builder.HasIndex(item => new { item.TenantId, item.OccurredAt, item.Id });
    }
}
