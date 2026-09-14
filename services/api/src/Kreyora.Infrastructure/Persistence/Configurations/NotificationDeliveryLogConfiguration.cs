using Kreyora.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class NotificationDeliveryLogConfiguration : IEntityTypeConfiguration<NotificationDeliveryLog>
{
    public void Configure(EntityTypeBuilder<NotificationDeliveryLog> builder)
    {
        builder.ToTable("notification_delivery_logs");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasMaxLength(26);
        builder.Property(l => l.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(l => l.NotificationRequestId).IsRequired().HasMaxLength(26);
        builder.Property(l => l.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(l => l.RecipientRedacted).IsRequired().HasMaxLength(200);
        builder.Property(l => l.SubjectRendered).IsRequired();
        builder.Property(l => l.BodyRendered).IsRequired();
        builder.Property(l => l.RenderedAt).IsRequired();
        builder.Property(l => l.ProviderName).IsRequired().HasMaxLength(128);

        builder.HasIndex(l => new { l.TenantId, l.NotificationRequestId });
        builder.HasIndex(l => new { l.TenantId, l.RenderedAt });
    }
}

