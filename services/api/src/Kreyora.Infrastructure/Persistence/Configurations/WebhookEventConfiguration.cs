using Kreyora.Domain.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.ToTable("webhook_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(26);
        builder.Property(e => e.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.ConnectionId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.ProviderEventId).IsRequired().HasMaxLength(WebhookEvent.ProviderEventIdMaxLength);
        builder.Property(e => e.EventType).HasMaxLength(WebhookEvent.EventTypeMaxLength);
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.ReceivedAt).IsRequired();
        builder.Property(e => e.ProcessedAt);
        builder.Property(e => e.ProcessingStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.CorrelationId).IsRequired().HasMaxLength(WebhookEvent.CorrelationIdMaxLength);
        builder.Property(e => e.Headers).HasColumnType("text").IsRequired();
        builder.Property(e => e.RawPayload).HasColumnType("text").IsRequired();
        builder.Property(e => e.IsPurged).HasDefaultValue(false).IsRequired();
        builder.Property(e => e.PurgedAt);
        builder.Property(e => e.ErrorMessage).HasMaxLength(WebhookEvent.ErrorMessageMaxLength);
        builder.Property(e => e.AttemptCount).HasDefaultValue(0).IsRequired();
        builder.Property(e => e.MaxAttempts).HasDefaultValue(5).IsRequired();
        builder.Property(e => e.NextRetryAt);
        builder.Property(e => e.DeadLetteredAt);
        builder.Property(e => e.LastAttemptedAt);
        builder.Property(e => e.FailureClassification).HasConversion<string>().HasMaxLength(32);

        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        // Unique deduplication index per connection
        builder.HasIndex(e => new { e.ConnectionId, e.ProviderEventId }).IsUnique();

        // Background worker query indexes
        builder.HasIndex(e => new { e.TenantId, e.ProcessingStatus, e.ReceivedAt });
        builder.HasIndex(e => new { e.TenantId, e.ProcessingStatus, e.NextRetryAt });

        // ADR-012 purge maintenance query index
        builder.HasIndex(e => new { e.IsPurged, e.ReceivedAt });

        builder.HasOne<ChannelConnection>()
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.ConnectionId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}

