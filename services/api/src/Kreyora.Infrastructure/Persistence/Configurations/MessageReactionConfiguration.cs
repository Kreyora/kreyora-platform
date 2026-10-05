using Kreyora.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class MessageReactionConfiguration : IEntityTypeConfiguration<MessageReaction>
{
    public void Configure(EntityTypeBuilder<MessageReaction> builder)
    {
        builder.ToTable("message_reactions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(26);
        builder.Property(e => e.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.ConnectionId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.ProviderMessageId).IsRequired().HasMaxLength(Message.ProviderMessageIdMaxLength);
        builder.Property(e => e.ReactorChannelId).IsRequired().HasMaxLength(MessageReaction.ReactorChannelIdMaxLength);
        builder.Property(e => e.Emoji).IsRequired().HasMaxLength(MessageReaction.EmojiMaxLength);
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.ModifiedAt).IsRequired();

        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        // Current reaction state per reactor per provider message (last write wins).
        builder.HasIndex(e => new { e.ConnectionId, e.ProviderMessageId, e.ReactorChannelId }).IsUnique();
    }
}
