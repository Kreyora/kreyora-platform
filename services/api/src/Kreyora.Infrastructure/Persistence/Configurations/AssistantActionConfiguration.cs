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

/// <summary>The redacted AI action log (M09-S06): JSONB metadata, a running-turn lease per conversation.</summary>
public sealed class AssistantTurnConfiguration : IEntityTypeConfiguration<AssistantTurn>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<AssistantTurn> builder)
    {
        builder.ToTable("assistant_turns");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasMaxLength(26);
        builder.Property(t => t.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(t => t.ConversationId).HasMaxLength(26);
        builder.Property(t => t.TriggerMessageId).HasMaxLength(26);
        builder.Property(t => t.TurnKey).IsRequired().HasMaxLength(80);
        builder.Property(t => t.Outcome).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.ReasonCode).IsRequired().HasMaxLength(AssistantTurn.ReasonMaxLength);
        builder.Property(t => t.PolicyVersion).HasMaxLength(32);
        builder.Property(t => t.PromptVersion).HasMaxLength(64);
        builder.Property(t => t.RegistryVersion).HasMaxLength(32);
        builder.Property(t => t.OutboundMessageId).HasMaxLength(26);
        AsJson<TurnModelCall>(builder.Property(t => t.ModelCalls));
        AsJson<TurnToolStep>(builder.Property(t => t.ToolSteps));
        AsJson<TurnCitation>(builder.Property(t => t.Citations));
        AsJson<string>(builder.Property(t => t.ValidationCodes));
        builder.HasIndex(t => new { t.TenantId, t.TurnKey }).IsUnique();
        builder.HasIndex(t => new { t.TenantId, t.ConversationId })
            .IsUnique().HasFilter("outcome = 'Running' AND conversation_id IS NOT NULL")
            .HasDatabaseName("ux_assistant_turns_running_per_conversation");
        builder.HasIndex(t => new { t.TenantId, t.StartedAt });
        builder.HasIndex(t => t.StartedAt);
        builder.HasOne<Conversation>().WithMany()
            .HasForeignKey(t => new { t.TenantId, t.ConversationId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .OnDelete(DeleteBehavior.Cascade);
        // No xmin token: the running-turn lease is protected by the unique indexes and a serializable transaction, and
        // the finished row is written whole (it may be re-attached after other services cleared the change tracker).
    }

    private static void AsJson<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<List<T>> property) =>
        property.HasColumnType("jsonb").HasConversion(
            value => JsonSerializer.Serialize(value, JsonOptions),
            json => JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>(),
            new ValueComparer<List<T>>(
                (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
                list => JsonSerializer.Serialize(list, JsonOptions).GetHashCode(StringComparison.Ordinal),
                list => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(list, JsonOptions), JsonOptions)!));
}
