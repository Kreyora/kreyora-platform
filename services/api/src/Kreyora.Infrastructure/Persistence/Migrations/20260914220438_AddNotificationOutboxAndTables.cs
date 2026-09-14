using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationOutboxAndTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_delivery_logs",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    notification_request_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    recipient_redacted = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subject_rendered = table.Column<string>(type: "text", nullable: false),
                    body_rendered = table.Column<string>(type: "text", nullable: false),
                    rendered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    provider_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_delivery_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_requests",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    source_event_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_event_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    template_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    template_version = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    recipient_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    recipient_contact = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_retry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dead_lettered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_redacted_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_requests", x => x.id);
                    table.UniqueConstraint("ak_notification_requests_tenant_id_id", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "notification_delivery_attempts",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    notification_request_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    provider_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    redacted_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    provider_reference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_delivery_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_delivery_attempts_notification_requests_tenant",
                        columns: x => new { x.tenant_id, x.notification_request_id },
                        principalTable: "notification_requests",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_delivery_attempts_tenant_id_notification_reque",
                table: "notification_delivery_attempts",
                columns: new[] { "tenant_id", "notification_request_id", "attempt_number" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_delivery_attempts_tenant_id_started_at",
                table: "notification_delivery_attempts",
                columns: new[] { "tenant_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_delivery_logs_tenant_id_notification_request_id",
                table: "notification_delivery_logs",
                columns: new[] { "tenant_id", "notification_request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_delivery_logs_tenant_id_rendered_at",
                table: "notification_delivery_logs",
                columns: new[] { "tenant_id", "rendered_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_requests_tenant_id_idempotency_key",
                table: "notification_requests",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_requests_tenant_id_source_event_id",
                table: "notification_requests",
                columns: new[] { "tenant_id", "source_event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_requests_tenant_id_status_next_retry_at",
                table: "notification_requests",
                columns: new[] { "tenant_id", "status", "next_retry_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_delivery_attempts");

            migrationBuilder.DropTable(
                name: "notification_delivery_logs");

            migrationBuilder.DropTable(
                name: "notification_requests");
        }
    }
}
