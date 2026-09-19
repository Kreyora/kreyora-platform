using Kreyora.Domain.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class InboundEventConfiguration : IEntityTypeConfiguration<InboundEvent>
{
    public void Configure(EntityTypeBuilder<InboundEvent> builder)
    {
        builder.ToTable("inbound_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(26);
        builder.Property(e => e.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.ConnectionId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.WebhookEventId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.ProviderMessageId).HasMaxLength(InboundEvent.ProviderMessageIdMaxLength);
        builder.Property(e => e.SchemaVersion).IsRequired().HasMaxLength(InboundEvent.SchemaVersionMaxLength);
        builder.Property(e => e.EventType).IsRequired().HasMaxLength(InboundEvent.EventTypeMaxLength);
        builder.Property(e => e.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.ModifiedAt).IsRequired();

        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        // Preserves the immutable raw event reference
        builder.HasOne<WebhookEvent>()
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.WebhookEventId })
            .HasPrincipalKey(w => new { w.TenantId, w.Id })
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // Unique deduplication index per connection for normalized messages
        builder.HasIndex(e => new { e.ConnectionId, e.ProviderMessageId })
            .IsUnique()
            .HasFilter("provider_message_id IS NOT NULL");

        builder.HasIndex(e => new { e.TenantId, e.WebhookEventId });
        builder.HasIndex(e => new { e.TenantId, e.OccurredAt });
    }
}

