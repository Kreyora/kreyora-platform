using Kreyora.Domain.Conversations;
using Kreyora.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(26);
        builder.Property(e => e.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.ConnectionId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.StoreId).HasMaxLength(26);
        builder.Property(e => e.CustomerChannelIdentityId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.AutomationMode).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.AssignedUserId).HasMaxLength(64);
        builder.Property(e => e.UnreadCount).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.ModifiedAt).IsRequired();
        builder.Ignore(e => e.IsAutomationActive);

        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        builder.HasOne<CustomerChannelIdentity>()
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.CustomerChannelIdentityId })
            .HasPrincipalKey(i => new { i.TenantId, i.Id })
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // One continuous thread per customer identity per connection (ADR-016).
        builder.HasIndex(e => new { e.ConnectionId, e.CustomerChannelIdentityId }).IsUnique();

        // Inbox list ordering and filters.
        builder.HasIndex(e => new { e.TenantId, e.LastMessageAt });
        builder.HasIndex(e => new { e.TenantId, e.Status });
        builder.HasIndex(e => new { e.TenantId, e.AssignedUserId }).HasFilter("assigned_user_id IS NOT NULL");
    }
}
