using Kreyora.Domain.Assistant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        builder.ToTable("knowledge_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasMaxLength(26);
        builder.Property(d => d.TenantId).IsRequired().HasMaxLength(26);
        builder.HasAlternateKey(d => new { d.TenantId, d.Id });
        builder.Property(d => d.Title).IsRequired().HasMaxLength(KnowledgeDocument.TitleMaxLength);
        builder.Property(d => d.Category).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.Source).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.StorePolicyKind).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.ActiveVersionId).HasMaxLength(26);
        builder.Ignore(d => d.IsDeleted);
        builder.HasIndex(d => new { d.TenantId, d.DeletedAt });
        // One live import per store policy kind per workspace.
        builder.HasIndex(d => new { d.TenantId, d.StorePolicyKind })
            .IsUnique()
            .HasFilter("store_policy_kind IS NOT NULL AND deleted_at IS NULL");
        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
    }
}

public sealed class KnowledgeDocumentVersionConfiguration : IEntityTypeConfiguration<KnowledgeDocumentVersion>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocumentVersion> builder)
    {
        builder.ToTable("knowledge_document_versions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasMaxLength(26);
        builder.Property(v => v.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(v => v.DocumentId).IsRequired().HasMaxLength(26);
        builder.Property(v => v.State).HasConversion<string>().HasMaxLength(32);
        builder.Property(v => v.ContentText).HasColumnType("text");
        builder.Property(v => v.ContentHash).IsRequired().HasMaxLength(64);
        builder.Property(v => v.OriginalObjectKey).HasMaxLength(512);
        builder.Property(v => v.OriginalFileName).HasMaxLength(255);
        builder.Property(v => v.IdempotencyKey).HasMaxLength(128);
        builder.Property(v => v.SubmittedByUserId).IsRequired().HasMaxLength(450);
        builder.Property(v => v.ReviewedByUserId).HasMaxLength(450);
        builder.Property(v => v.ReviewNote).HasMaxLength(KnowledgeDocumentVersion.ReviewNoteMaxLength);
        builder.HasAlternateKey(v => new { v.TenantId, v.Id });

        builder.HasOne<KnowledgeDocument>()
            .WithMany()
            .HasForeignKey(v => new { v.TenantId, v.DocumentId })
            .HasPrincipalKey(d => new { d.TenantId, d.Id })
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(v => new { v.DocumentId, v.VersionNumber }).IsUnique();
        // At most one Active version per document: concurrent approvals cannot both win.
        builder.HasIndex(v => v.DocumentId).IsUnique().HasFilter("state = 'Active'").HasDatabaseName("ix_knowledge_document_versions_one_active");
        // Retried submissions with the same key return the original version.
        builder.HasIndex(v => new { v.TenantId, v.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        builder.HasIndex(v => new { v.TenantId, v.State });
        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
    }
}

public sealed class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    public void Configure(EntityTypeBuilder<KnowledgeChunk> builder)
    {
        builder.ToTable("knowledge_chunks");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasMaxLength(26);
        builder.Property(c => c.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(c => c.DocumentId).IsRequired().HasMaxLength(26);
        builder.Property(c => c.VersionId).IsRequired().HasMaxLength(26);
        builder.Property(c => c.Text).IsRequired().HasColumnType("text");
        builder.Property(c => c.ContentHash).IsRequired().HasMaxLength(64);
        builder.Property(c => c.Embedding).HasColumnType("real[]");
        builder.Property(c => c.EmbeddingModel).HasMaxLength(128);

        builder.HasOne<KnowledgeDocumentVersion>()
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.VersionId })
            .HasPrincipalKey(v => new { v.TenantId, v.Id })
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.VersionId, c.ChunkIndex }).IsUnique();
        builder.HasIndex(c => new { c.TenantId, c.VersionId });
    }
}
