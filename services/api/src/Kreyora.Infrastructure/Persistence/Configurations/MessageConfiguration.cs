using Kreyora.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(26);
        builder.Property(e => e.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.ConversationId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.ConnectionId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.InboundEventId).HasMaxLength(26);
        builder.Property(e => e.OutboundMessageId).HasMaxLength(26);
        builder.Property(e => e.ActorUserId).HasMaxLength(64);
        builder.Ignore(e => e.IsPending);
        builder.Property(e => e.Direction).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(e => e.Origin).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.ProviderMessageId).HasMaxLength(Message.ProviderMessageIdMaxLength);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(e => e.Text);
        builder.Property(e => e.MediaUrl);
        builder.Property(e => e.MediaContentType).HasMaxLength(Message.MediaContentTypeMaxLength);
        builder.Property(e => e.DeliveryStatus).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.ReceivedAt).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.ModifiedAt).IsRequired();

        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.ConversationId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // A provider message exists once per connection: sends and their echoes never duplicate.
        builder.HasIndex(e => new { e.ConnectionId, e.ProviderMessageId })
            .IsUnique()
            .HasFilter("provider_message_id IS NOT NULL");

        // Timeline keyset: provider time, then insertion, then ID.
        builder.HasIndex(e => new { e.ConversationId, e.OccurredAt, e.CreatedAt, e.Id });
        builder.HasIndex(e => new { e.TenantId, e.InboundEventId }).HasFilter("inbound_event_id IS NOT NULL");

        // One timeline entry per outbox message (staff reply reconciliation, ADR-017).
        builder.HasIndex(e => e.OutboundMessageId).IsUnique().HasFilter("outbound_message_id IS NOT NULL");
    }
}
