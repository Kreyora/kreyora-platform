using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantPolicyAndKnowledge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assistant_policies",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    reply_style = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    supported_languages = table.Column<List<string>>(type: "text[]", nullable: false),
                    tone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    brand_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    business_hours = table.Column<string>(type: "jsonb", nullable: false),
                    outside_hours_behavior = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    unrecognized_media_behavior = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    escalation_keywords = table.Column<List<string>>(type: "text[]", nullable: false),
                    allowed_tools = table.Column<List<string>>(type: "text[]", nullable: false),
                    max_tool_steps = table.Column<int>(type: "integer", nullable: false),
                    max_replies_per_conversation_per_hour = table.Column<int>(type: "integer", nullable: false),
                    max_output_tokens = table.Column<int>(type: "integer", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_documents",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    store_policy_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    active_version_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    latest_version_number = table.Column<int>(type: "integer", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_documents", x => x.id);
                    table.UniqueConstraint("ak_knowledge_documents_tenant_id_id", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "knowledge_document_versions",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    document_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content_text = table.Column<string>(type: "text", nullable: true),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    character_count = table.Column<int>(type: "integer", nullable: false),
                    original_object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    original_byte_size = table.Column<long>(type: "bigint", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    submitted_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_document_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_knowledge_document_versions_knowledge_documents_tenant_id_d",
                        columns: x => new { x.tenant_id, x.document_id },
                        principalTable: "knowledge_documents",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_policies_tenant_id",
                table: "assistant_policies",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_document_versions_document_id_version_number",
                table: "knowledge_document_versions",
                columns: new[] { "document_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_document_versions_one_active",
                table: "knowledge_document_versions",
                column: "document_id",
                unique: true,
                filter: "state = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_document_versions_tenant_id_document_id",
                table: "knowledge_document_versions",
                columns: new[] { "tenant_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_document_versions_tenant_id_idempotency_key",
                table: "knowledge_document_versions",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_document_versions_tenant_id_state",
                table: "knowledge_document_versions",
                columns: new[] { "tenant_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_documents_tenant_id_deleted_at",
                table: "knowledge_documents",
                columns: new[] { "tenant_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_documents_tenant_id_store_policy_kind",
                table: "knowledge_documents",
                columns: new[] { "tenant_id", "store_policy_kind" },
                unique: true,
                filter: "store_policy_kind IS NOT NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_policies");

            migrationBuilder.DropTable(
                name: "knowledge_document_versions");

            migrationBuilder.DropTable(
                name: "knowledge_documents");
        }
    }
}
