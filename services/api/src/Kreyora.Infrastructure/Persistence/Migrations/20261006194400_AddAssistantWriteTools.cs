using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantWriteTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "escalated_at",
                table: "conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "escalation_category",
                table: "conversations",
                type: "character varying(48)",
                maxLength: 48,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "assistant_actions",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    conversation_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tool = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    arguments_fingerprint = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    result_json = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    internal_reference = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_assistant_actions_conversations_tenant_id_conversation_id",
                        columns: x => new { x.tenant_id, x.conversation_id },
                        principalTable: "conversations",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assistant_checkout_links",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    store_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    conversation_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    customer_channel_identity_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    lines_fingerprint = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    lines = table.Column<string>(type: "jsonb", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    checkout_session_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    order_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_checkout_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_assistant_checkout_links_conversations_tenant_id_conversati",
                        columns: x => new { x.tenant_id, x.conversation_id },
                        principalTable: "conversations",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_assistant_checkout_links_stores_tenant_id_store_id",
                        columns: x => new { x.tenant_id, x.store_id },
                        principalTable: "stores",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_actions_tenant_id_conversation_id_created_at",
                table: "assistant_actions",
                columns: new[] { "tenant_id", "conversation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_actions_tenant_id_idempotency_key",
                table: "assistant_actions",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assistant_checkout_links_tenant_id_checkout_session_id",
                table: "assistant_checkout_links",
                columns: new[] { "tenant_id", "checkout_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_checkout_links_tenant_id_conversation_id_state",
                table: "assistant_checkout_links",
                columns: new[] { "tenant_id", "conversation_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_checkout_links_tenant_id_store_id",
                table: "assistant_checkout_links",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_checkout_links_token_hash",
                table: "assistant_checkout_links",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_actions");

            migrationBuilder.DropTable(
                name: "assistant_checkout_links");

            migrationBuilder.DropColumn(
                name: "escalated_at",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "escalation_category",
                table: "conversations");
        }
    }
}
