using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookEventRetryAndInboundEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                table: "webhook_events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dead_lettered_at",
                table: "webhook_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "failure_classification",
                table: "webhook_events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_attempted_at",
                table: "webhook_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_attempts",
                table: "webhook_events",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_retry_at",
                table: "webhook_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inbound_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    connection_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    webhook_event_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    schema_version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_events", x => x.id);
                    table.UniqueConstraint("ak_inbound_events_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_inbound_events_webhook_events_tenant_id_webhook_event_id",
                        columns: x => new { x.tenant_id, x.webhook_event_id },
                        principalTable: "webhook_events",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_tenant_id_processing_status_next_retry_at",
                table: "webhook_events",
                columns: new[] { "tenant_id", "processing_status", "next_retry_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_events_connection_id_provider_message_id",
                table: "inbound_events",
                columns: new[] { "connection_id", "provider_message_id" },
                unique: true,
                filter: "provider_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_events_tenant_id_occurred_at",
                table: "inbound_events",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_events_tenant_id_webhook_event_id",
                table: "inbound_events",
                columns: new[] { "tenant_id", "webhook_event_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbound_events");

            migrationBuilder.DropIndex(
                name: "ix_webhook_events_tenant_id_processing_status_next_retry_at",
                table: "webhook_events");

            migrationBuilder.DropColumn(
                name: "attempt_count",
                table: "webhook_events");

            migrationBuilder.DropColumn(
                name: "dead_lettered_at",
                table: "webhook_events");

            migrationBuilder.DropColumn(
                name: "failure_classification",
                table: "webhook_events");

            migrationBuilder.DropColumn(
                name: "last_attempted_at",
                table: "webhook_events");

            migrationBuilder.DropColumn(
                name: "max_attempts",
                table: "webhook_events");

            migrationBuilder.DropColumn(
                name: "next_retry_at",
                table: "webhook_events");
        }
    }
}
