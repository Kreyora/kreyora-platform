using Kreyora.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class CheckoutSessionCommandConfiguration : IEntityTypeConfiguration<CheckoutSessionCommand>
{
    public void Configure(EntityTypeBuilder<CheckoutSessionCommand> builder)
    {
        builder.ToTable("checkout_session_commands");
        builder.HasKey(command => command.Id);
        builder.Property(command => command.Id).HasMaxLength(26);
        builder.Property(command => command.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(command => command.Operation).IsRequired().HasMaxLength(100);
        builder.Property(command => command.IdempotencyKey).IsRequired().HasMaxLength(256);
        builder.Property(command => command.RequestFingerprint).IsRequired().HasMaxLength(64);
        builder.Property(command => command.CheckoutSessionId).IsRequired().HasMaxLength(26);
        builder.HasIndex(command => new { command.TenantId, command.Operation, command.IdempotencyKey }).IsUnique();
    }
}
