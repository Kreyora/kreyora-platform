using Kreyora.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class StoreCommandIdempotencyConfiguration : IEntityTypeConfiguration<StoreCommandIdempotency>
{
    public void Configure(EntityTypeBuilder<StoreCommandIdempotency> builder)
    {
        builder.ToTable("store_command_idempotency");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasMaxLength(26);
        builder.Property(record => record.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(record => record.Operation).IsRequired().HasMaxLength(100);
        builder.Property(record => record.IdempotencyKey).IsRequired().HasMaxLength(256);
        builder.Property(record => record.RequestFingerprint).IsRequired().HasMaxLength(64);
        builder.Property(record => record.ResourceId).IsRequired().HasMaxLength(26);
        builder.Property(record => record.CreatedAt).IsRequired();
        builder.Property(record => record.ModifiedAt).IsRequired();
        builder.HasIndex(record => new { record.TenantId, record.Operation, record.IdempotencyKey }).IsUnique();
    }
}
