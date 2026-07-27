using Kreyora.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(26);
        builder.Property(x => x.MessageId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ConsumerName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ProcessedAt).IsRequired();

        builder.HasIndex(x => new { x.MessageId, x.ConsumerName })
            .IsUnique()
            .HasDatabaseName("ix_inbox_messages_message_consumer");
    }
}
