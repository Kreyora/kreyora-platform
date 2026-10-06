using System.Text.Json;
using Kreyora.Domain.Assistant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class AssistantPolicyConfiguration : IEntityTypeConfiguration<AssistantPolicy>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<AssistantPolicy> builder)
    {
        builder.ToTable("assistant_policies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasMaxLength(26);
        builder.Property(p => p.TenantId).IsRequired().HasMaxLength(26);
        builder.HasIndex(p => p.TenantId).IsUnique();

        builder.Property(p => p.ReplyStyle).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Tone).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.OutsideHoursBehavior).HasConversion<string>().HasMaxLength(48);
        builder.Property(p => p.UnrecognizedMediaBehavior).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.BrandNote).HasMaxLength(AssistantPolicy.BrandNoteMaxLength);
        builder.Property(p => p.SupportedLanguages).HasColumnType("text[]");
        builder.Property(p => p.EscalationKeywords).HasColumnType("text[]");
        builder.Property(p => p.AllowedTools).HasColumnType("text[]");
        builder.Property(p => p.BusinessHours)
            .HasColumnType("jsonb")
            .HasConversion(
                hours => JsonSerializer.Serialize(hours, Json),
                json => JsonSerializer.Deserialize<List<DailyHours>>(json, Json) ?? new List<DailyHours>(),
                new ValueComparer<List<DailyHours>>(
                    (a, b) => a!.SequenceEqual(b!),
                    list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                    list => list.ToList()));
        builder.Property(p => p.ReviewedByUserId).HasMaxLength(450);
        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
    }
}
