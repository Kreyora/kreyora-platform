using Kreyora.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class NotificationRequestConfiguration : IEntityTypeConfiguration<NotificationRequest>
{
    public void Configure(EntityTypeBuilder<NotificationRequest> builder)
    {
        builder.ToTable("notification_requests");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasMaxLength(26);
        builder.Property(n => n.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(n => n.SourceEventType).IsRequired().HasMaxLength(NotificationRequest.SourceEventTypeMaxLength);
        builder.Property(n => n.SourceEventId).IsRequired().HasMaxLength(26);
        builder.Property(n => n.TemplateCode).IsRequired().HasMaxLength(NotificationRequest.TemplateCodeMaxLength);
        builder.Property(n => n.TemplateVersion).IsRequired();
        builder.Property(n => n.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(n => n.RecipientName).HasMaxLength(NotificationRequest.RecipientNameMaxLength);
        builder.Property(n => n.RecipientContact).IsRequired().HasMaxLength(NotificationRequest.RecipientContactMaxLength);
        builder.Property(n => n.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(n => n.MaxAttempts).IsRequired();
        builder.Property(n => n.AttemptCount).IsRequired();
        builder.Property(n => n.LastRedactedError).HasMaxLength(NotificationRequest.LastRedactedErrorMaxLength);
        builder.Property(n => n.IdempotencyKey).IsRequired().HasMaxLength(NotificationRequest.IdempotencyKeyMaxLength);

        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();

        builder.HasAlternateKey(n => new { n.TenantId, n.Id });

        builder.HasIndex(n => new { n.TenantId, n.IdempotencyKey }).IsUnique();
        builder.HasIndex(n => new { n.TenantId, n.Status, n.NextRetryAt });
        builder.HasIndex(n => new { n.TenantId, n.SourceEventId });

        builder.HasMany(n => n.DeliveryAttempts)
            .WithOne()
            .HasForeignKey(a => new { a.TenantId, a.NotificationRequestId })
            .HasPrincipalKey(n => new { n.TenantId, n.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
