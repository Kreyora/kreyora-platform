using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    connection_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processing_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    headers = table.Column<string>(type: "text", nullable: false),
                    raw_payload = table.Column<string>(type: "text", nullable: false),
                    is_purged = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    purged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_events", x => x.id);
                    table.UniqueConstraint("ak_webhook_events_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_webhook_events_channel_connections_tenant_id_connection_id",
                        columns: x => new { x.tenant_id, x.connection_id },
                        principalTable: "channel_connections",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_connection_id_provider_event_id",
                table: "webhook_events",
                columns: new[] { "connection_id", "provider_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_is_purged_received_at",
                table: "webhook_events",
                columns: new[] { "is_purged", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_tenant_id_connection_id",
                table: "webhook_events",
                columns: new[] { "tenant_id", "connection_id" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_tenant_id_processing_status_received_at",
                table: "webhook_events",
                columns: new[] { "tenant_id", "processing_status", "received_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_events");
        }
    }
}
