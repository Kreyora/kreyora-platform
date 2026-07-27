using Kreyora.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");
        builder.HasKey(x => x.IdempotencyKey);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(256);
        builder.Property(x => x.ConsumerName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
    }
}
