using Kreyora.Domain.Audit;
using Kreyora.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events", table => table.HasCheckConstraint("ck_audit_events_actor_provenance", "(actor_kind = 'Member' AND actor_user_id IS NOT NULL) OR (actor_kind = 'CommerceSystem' AND actor_user_id IS NULL)"));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(26);
        builder.Property(item => item.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.ActorUserId).HasMaxLength(26);
        builder.Property(item => item.ActorKind).HasConversion<string>().HasMaxLength(32).HasDefaultValue(CommerceActorKind.Member).IsRequired();
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
