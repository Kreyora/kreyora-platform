using Kreyora.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class NotificationDeliveryAttemptConfiguration : IEntityTypeConfiguration<NotificationDeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<NotificationDeliveryAttempt> builder)
    {
        builder.ToTable("notification_delivery_attempts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasMaxLength(26);
        builder.Property(a => a.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(a => a.NotificationRequestId).IsRequired().HasMaxLength(26);
        builder.Property(a => a.AttemptNumber).IsRequired();
        builder.Property(a => a.ProviderName).IsRequired().HasMaxLength(NotificationDeliveryAttempt.ProviderNameMaxLength);
        builder.Property(a => a.StartedAt).IsRequired();
        builder.Property(a => a.Succeeded).IsRequired();
        builder.Property(a => a.RedactedError).HasMaxLength(NotificationDeliveryAttempt.RedactedErrorMaxLength);
        builder.Property(a => a.ProviderReference).HasMaxLength(NotificationDeliveryAttempt.ProviderReferenceMaxLength);

        builder.HasIndex(a => new { a.TenantId, a.NotificationRequestId, a.AttemptNumber });
        builder.HasIndex(a => new { a.TenantId, a.StartedAt });
    }
}

