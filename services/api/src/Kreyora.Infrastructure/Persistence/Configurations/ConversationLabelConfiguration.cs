using Kreyora.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class ConversationLabelConfiguration : IEntityTypeConfiguration<ConversationLabel>
{
    public void Configure(EntityTypeBuilder<ConversationLabel> builder)
    {
        builder.ToTable("conversation_labels");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(26);
        builder.Property(e => e.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.ConversationId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.Label).IsRequired().HasMaxLength(ConversationLabel.LabelMaxLength);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.ModifiedAt).IsRequired();

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.ConversationId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.ConversationId, e.Label }).IsUnique();
    }
}
