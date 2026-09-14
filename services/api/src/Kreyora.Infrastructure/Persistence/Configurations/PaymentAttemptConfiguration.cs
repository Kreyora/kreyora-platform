using Kreyora.Domain.Orders;
using Kreyora.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable("payment_attempts", table =>
        {
            table.HasCheckConstraint("ck_payment_attempts_amount_npr", "amount_npr >= 0");
        });

        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.Id).HasMaxLength(26);
        builder.Property(attempt => attempt.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(attempt => attempt.OrderId).IsRequired().HasMaxLength(26);
        builder.Property(attempt => attempt.Method).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(attempt => attempt.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(attempt => attempt.AmountNpr).HasPrecision(18, 2).IsRequired();
        builder.Property(attempt => attempt.Currency).IsRequired().HasMaxLength(3);
        builder.Property(attempt => attempt.InternalReference).IsRequired().HasMaxLength(PaymentAttempt.ReferenceMaxLength);
        builder.Property(attempt => attempt.ProviderReference).HasMaxLength(PaymentAttempt.ProviderReferenceMaxLength);
        builder.Property(attempt => attempt.VerifiedByUserId).HasMaxLength(26);
        builder.Property(attempt => attempt.RejectedByUserId).HasMaxLength(26);
        builder.Property(attempt => attempt.RejectionReason).HasMaxLength(PaymentAttempt.ReasonMaxLength);
        builder.Property(attempt => attempt.CollectedByUserId).HasMaxLength(26);

        builder.HasAlternateKey(attempt => new { attempt.TenantId, attempt.Id });
        builder.HasIndex(attempt => new { attempt.TenantId, attempt.InternalReference }).IsUnique();
        builder.HasIndex(attempt => new { attempt.TenantId, attempt.OrderId });
        builder.HasIndex(attempt => new { attempt.TenantId, attempt.Status });

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(attempt => new { attempt.TenantId, attempt.OrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(attempt => attempt.Proofs)
            .WithOne()
            .HasForeignKey(proof => new { proof.TenantId, proof.PaymentAttemptId })
            .HasPrincipalKey(attempt => new { attempt.TenantId, attempt.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

