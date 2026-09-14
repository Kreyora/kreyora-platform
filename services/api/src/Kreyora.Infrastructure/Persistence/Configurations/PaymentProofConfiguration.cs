using Kreyora.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class PaymentProofConfiguration : IEntityTypeConfiguration<PaymentProof>
{
    public void Configure(EntityTypeBuilder<PaymentProof> builder)
    {
        builder.ToTable("payment_proofs", table =>
        {
            table.HasCheckConstraint("ck_payment_proofs_byte_size", "byte_size > 0");
        });

        builder.HasKey(proof => proof.Id);
        builder.Property(proof => proof.Id).HasMaxLength(26);
        builder.Property(proof => proof.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(proof => proof.PaymentAttemptId).IsRequired().HasMaxLength(26);
        builder.Property(proof => proof.ObjectKey).IsRequired().HasMaxLength(PaymentProof.ObjectKeyMaxLength);
        builder.Property(proof => proof.ContentType).IsRequired().HasMaxLength(PaymentProof.ContentTypeMaxLength);
        builder.Property(proof => proof.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(proof => proof.CustomerNote).HasMaxLength(PaymentProof.NoteMaxLength);

        builder.HasAlternateKey(proof => new { proof.TenantId, proof.Id });
        builder.HasIndex(proof => new { proof.TenantId, proof.ObjectKey }).IsUnique();
        builder.HasIndex(proof => new { proof.TenantId, proof.PaymentAttemptId });
        builder.HasIndex(proof => new { proof.TenantId, proof.Status, proof.UploadExpiresAt });
    }
}

