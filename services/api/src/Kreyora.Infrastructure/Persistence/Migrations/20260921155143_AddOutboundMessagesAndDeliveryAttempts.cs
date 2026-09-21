using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundMessagesAndDeliveryAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbound_messages",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    connection_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    conversation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    recipient_channel_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    message_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    text_content = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    media_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    media_content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    caption = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    template_code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    template_parameters_json = table.Column<string>(type: "jsonb", nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    next_retry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_classification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    last_error_message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dead_lettered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbound_messages", x => x.id);
                    table.UniqueConstraint("ak_outbound_messages_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_outbound_messages_channel_connections_tenant_id_connection_",
                        columns: x => new { x.tenant_id, x.connection_id },
                        principalTable: "channel_connections",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbound_delivery_attempts",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    outbound_message_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    connection_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    provider_error_code = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    provider_error_message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbound_delivery_attempts", x => x.id);
                    table.UniqueConstraint("ak_outbound_delivery_attempts_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_outbound_delivery_attempts_outbound_messages_tenant_id_outb",
                        columns: x => new { x.tenant_id, x.outbound_message_id },
                        principalTable: "outbound_messages",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_delivery_attempts_outbound_message_id_attempt_numb",
                table: "outbound_delivery_attempts",
                columns: new[] { "outbound_message_id", "attempt_number" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_delivery_attempts_tenant_id_outbound_message_id",
                table: "outbound_delivery_attempts",
                columns: new[] { "tenant_id", "outbound_message_id" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_delivery_attempts_tenant_id_started_at",
                table: "outbound_delivery_attempts",
                columns: new[] { "tenant_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_messages_by_status",
                table: "outbound_messages",
                columns: new[] { "tenant_id", "status", "queued_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_messages_delivery_queue",
                table: "outbound_messages",
                columns: new[] { "tenant_id", "status", "next_retry_at" },
                filter: "status IN ('Queued', 'Failed')");

            migrationBuilder.CreateIndex(
                name: "ix_outbound_messages_idempotency",
                table: "outbound_messages",
                columns: new[] { "tenant_id", "connection_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_messages_provider_msg",
                table: "outbound_messages",
                columns: new[] { "connection_id", "provider_message_id" },
                filter: "provider_message_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbound_delivery_attempts");

            migrationBuilder.DropTable(
                name: "outbound_messages");
        }
    }
}
