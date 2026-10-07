using System.Text.Json;
using Kreyora.Domain.Assistant;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

/// <summary>Write-tool proposals and completed calls (M09-S05).</summary>
public sealed class AssistantActionConfiguration : IEntityTypeConfiguration<AssistantAction>
{
    public void Configure(EntityTypeBuilder<AssistantAction> builder)
    {
        builder.ToTable("assistant_actions");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasMaxLength(26);
        builder.Property(a => a.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(a => a.ConversationId).IsRequired().HasMaxLength(26);
        builder.Property(a => a.Tool).IsRequired().HasMaxLength(64);
        builder.Property(a => a.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(a => a.ArgumentsFingerprint).IsRequired().HasMaxLength(512);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.ResultJson).HasMaxLength(AssistantAction.ResultMaxLength);
        builder.Property(a => a.InternalReference).HasMaxLength(4000);
        builder.HasIndex(a => new { a.TenantId, a.IdempotencyKey }).IsUnique();
        builder.HasIndex(a => new { a.TenantId, a.ConversationId, a.CreatedAt });
        builder.HasOne<Conversation>().WithMany()
            .HasForeignKey(a => new { a.TenantId, a.ConversationId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
    }
}

/// <summary>Assistant checkout links (M09-S05): hashed token, lines as JSON, lifecycle.</summary>
public sealed class AssistantCheckoutLinkConfiguration : IEntityTypeConfiguration<AssistantCheckoutLink>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<AssistantCheckoutLink> builder)
    {
        builder.ToTable("assistant_checkout_links");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasMaxLength(26);
        builder.Property(l => l.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(l => l.StoreId).IsRequired().HasMaxLength(26);
        builder.Property(l => l.ConversationId).IsRequired().HasMaxLength(26);
        builder.Property(l => l.CustomerChannelIdentityId).IsRequired().HasMaxLength(26);
        builder.Property(l => l.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(l => l.LinesFingerprint).IsRequired().HasMaxLength(512);
        builder.Property(l => l.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(l => l.CheckoutSessionId).HasMaxLength(26);
        builder.Property(l => l.OrderId).HasMaxLength(26);
        builder.Property(l => l.Lines)
            .HasColumnType("jsonb")
            .HasConversion(
                lines => JsonSerializer.Serialize(lines, Json),
                json => JsonSerializer.Deserialize<List<CheckoutLinkLine>>(json, Json) ?? new List<CheckoutLinkLine>(),
                new ValueComparer<List<CheckoutLinkLine>>(
                    (a, b) => a!.SequenceEqual(b!),
                    list => list.Aggregate(0, (hash, line) => HashCode.Combine(hash, line.GetHashCode())),
                    list => list.ToList()));
        builder.HasIndex(l => l.TokenHash).IsUnique();
        builder.HasIndex(l => new { l.TenantId, l.ConversationId, l.State });
        builder.HasIndex(l => new { l.TenantId, l.CheckoutSessionId });
        builder.HasOne<Conversation>().WithMany()
            .HasForeignKey(l => new { l.TenantId, l.ConversationId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Store>().WithMany()
            .HasForeignKey(l => new { l.TenantId, l.StoreId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
    }
}
