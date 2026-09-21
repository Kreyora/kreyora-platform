using Kreyora.Domain.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class OutboundMessageConfiguration : IEntityTypeConfiguration<OutboundMessage>
{
    public void Configure(EntityTypeBuilder<OutboundMessage> builder)
    {
        builder.ToTable("outbound_messages");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(26);
        builder.Property(e => e.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.ConnectionId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.ConversationId).HasMaxLength(OutboundMessage.ConversationIdMaxLength);
        builder.Property(e => e.RecipientChannelId).IsRequired().HasMaxLength(OutboundMessage.RecipientChannelIdMaxLength);
        builder.Property(e => e.IdempotencyKey).IsRequired().HasMaxLength(OutboundMessage.IdempotencyKeyMaxLength);
        builder.Property(e => e.MessageType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.TextContent).HasMaxLength(OutboundMessage.TextContentMaxLength);
        builder.Property(e => e.MediaUrl).HasMaxLength(OutboundMessage.MediaUrlMaxLength);
        builder.Property(e => e.MediaContentType).HasMaxLength(OutboundMessage.MediaContentTypeMaxLength);
        builder.Property(e => e.Caption).HasMaxLength(OutboundMessage.CaptionMaxLength);
        builder.Property(e => e.TemplateCode).HasMaxLength(OutboundMessage.TemplateCodeMaxLength);
        builder.Property(e => e.TemplateParametersJson).HasColumnType("jsonb");
        builder.Property(e => e.MetadataJson).HasColumnType("jsonb");
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.ProviderMessageId).HasMaxLength(OutboundMessage.ProviderMessageIdMaxLength);
        builder.Property(e => e.AttemptCount).HasDefaultValue(0).IsRequired();
        builder.Property(e => e.MaxAttempts).HasDefaultValue(5).IsRequired();
        builder.Property(e => e.NextRetryAt);
        builder.Property(e => e.FailureClassification).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.LastErrorMessage).HasMaxLength(OutboundMessage.LastErrorMessageMaxLength);
        builder.Property(e => e.QueuedAt).IsRequired();
        builder.Property(e => e.SentAt);
        builder.Property(e => e.DeliveredAt);
        builder.Property(e => e.ReadAt);
        builder.Property(e => e.FailedAt);
        builder.Property(e => e.DeadLetteredAt);
        builder.Property(e => e.CancelledAt);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.ModifiedAt).IsRequired();

        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        // Idempotency constraint per tenant and connection
        builder.HasIndex(e => new { e.TenantId, e.ConnectionId, e.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ix_outbound_messages_idempotency");

        // Delivery queue query index
        builder.HasIndex(e => new { e.TenantId, e.Status, e.NextRetryAt })
            .HasFilter("status IN ('Queued', 'Failed')")
            .HasDatabaseName("ix_outbound_messages_delivery_queue");

        // Query by status and time
        builder.HasIndex(e => new { e.TenantId, e.Status, e.QueuedAt })
            .HasDatabaseName("ix_outbound_messages_by_status");

        // Receipt status matching index
        builder.HasIndex(e => new { e.ConnectionId, e.ProviderMessageId })
            .HasFilter("provider_message_id IS NOT NULL")
            .HasDatabaseName("ix_outbound_messages_provider_msg");

        builder.HasOne<ChannelConnection>()
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.ConnectionId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}

