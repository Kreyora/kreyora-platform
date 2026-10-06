using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_suspicious_instructions",
                table: "knowledge_document_versions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_knowledge_document_versions_tenant_id_id",
                table: "knowledge_document_versions",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateTable(
                name: "knowledge_chunks",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    document_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    version_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    char_start = table.Column<int>(type: "integer", nullable: false),
                    char_end = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    embedding = table.Column<float[]>(type: "real[]", nullable: true),
                    embedding_model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    embedding_dimensions = table.Column<int>(type: "integer", nullable: true),
                    indexed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_chunks", x => x.id);
                    table.ForeignKey(
                        name: "fk_knowledge_chunks_knowledge_document_versions_tenant_id_vers",
                        columns: x => new { x.tenant_id, x.version_id },
                        principalTable: "knowledge_document_versions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_chunks_tenant_id_version_id",
                table: "knowledge_chunks",
                columns: new[] { "tenant_id", "version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_chunks_version_id_chunk_index",
                table: "knowledge_chunks",
                columns: new[] { "version_id", "chunk_index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_chunks");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_knowledge_document_versions_tenant_id_id",
                table: "knowledge_document_versions");

            migrationBuilder.DropColumn(
                name: "has_suspicious_instructions",
                table: "knowledge_document_versions");
        }
    }
}
