using Kreyora.Domain.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class OutboundDeliveryAttemptConfiguration : IEntityTypeConfiguration<OutboundDeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<OutboundDeliveryAttempt> builder)
    {
        builder.ToTable("outbound_delivery_attempts");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(26);
        builder.Property(e => e.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.OutboundMessageId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.AttemptNumber).IsRequired();
        builder.Property(e => e.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.ConnectionId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.StartedAt).IsRequired();
        builder.Property(e => e.CompletedAt);
        builder.Property(e => e.Succeeded).IsRequired();
        builder.Property(e => e.ProviderMessageId).HasMaxLength(OutboundDeliveryAttempt.ProviderMessageIdMaxLength);
        builder.Property(e => e.ProviderErrorCode).HasMaxLength(OutboundDeliveryAttempt.ProviderErrorCodeMaxLength);
        builder.Property(e => e.ProviderErrorMessage).HasMaxLength(OutboundDeliveryAttempt.ProviderErrorMessageMaxLength);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.ModifiedAt).IsRequired();

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        builder.HasIndex(e => new { e.OutboundMessageId, e.AttemptNumber });
        builder.HasIndex(e => new { e.TenantId, e.StartedAt });

        builder.HasOne<OutboundMessage>()
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.OutboundMessageId })
            .HasPrincipalKey(m => new { m.TenantId, m.Id })
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}

