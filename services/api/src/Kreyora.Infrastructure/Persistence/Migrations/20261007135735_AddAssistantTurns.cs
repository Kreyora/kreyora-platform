using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantTurns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assistant_turns",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    conversation_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    trigger_message_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    turn_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_playground = table.Column<bool>(type: "boolean", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    policy_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    registry_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    model_calls = table.Column<string>(type: "jsonb", nullable: false),
                    tool_steps = table.Column<string>(type: "jsonb", nullable: false),
                    citations = table.Column<string>(type: "jsonb", nullable: false),
                    validation_codes = table.Column<string>(type: "jsonb", nullable: false),
                    model_call_count = table.Column<int>(type: "integer", nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_tokens = table.Column<int>(type: "integer", nullable: false),
                    estimated_cost_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    outbound_message_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_turns", x => x.id);
                    table.ForeignKey(
                        name: "fk_assistant_turns_conversations_tenant_id_conversation_id",
                        columns: x => new { x.tenant_id, x.conversation_id },
                        principalTable: "conversations",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_turns_started_at",
                table: "assistant_turns",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_turns_tenant_id_started_at",
                table: "assistant_turns",
                columns: new[] { "tenant_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_turns_tenant_id_turn_key",
                table: "assistant_turns",
                columns: new[] { "tenant_id", "turn_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_assistant_turns_running_per_conversation",
                table: "assistant_turns",
                columns: new[] { "tenant_id", "conversation_id" },
                unique: true,
                filter: "outcome = 'Running' AND conversation_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_turns");
        }
    }
}
