using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationsMessagesAndChannelIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_channel_identities",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    connection_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    customer_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erased_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_channel_identities", x => x.id);
                    table.UniqueConstraint("ak_customer_channel_identities_tenant_id_id", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "message_reactions",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    connection_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    reactor_channel_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    emoji = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_removed = table.Column<bool>(type: "boolean", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_reactions", x => x.id);
                    table.UniqueConstraint("ak_message_reactions_tenant_id_id", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    connection_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    store_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    customer_channel_identity_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    automation_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    assigned_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    unread_count = table.Column<int>(type: "integer", nullable: false),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_customer_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    customer_last_read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                    table.UniqueConstraint("ak_conversations_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_conversations_customer_channel_identities_tenant_id_custome",
                        columns: x => new { x.tenant_id, x.customer_channel_identity_id },
                        principalTable: "customer_channel_identities",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conversation_labels",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    conversation_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    label = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_labels", x => x.id);
                    table.UniqueConstraint("ak_conversation_labels_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_conversation_labels_conversations_tenant_id_conversation_id",
                        columns: x => new { x.tenant_id, x.conversation_id },
                        principalTable: "conversations",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    conversation_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    connection_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    inbound_event_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    text = table.Column<string>(type: "text", nullable: true),
                    media_url = table.Column<string>(type: "text", nullable: true),
                    media_content_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivery_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    redacted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.UniqueConstraint("ak_messages_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_messages_conversations_tenant_id_conversation_id",
                        columns: x => new { x.tenant_id, x.conversation_id },
                        principalTable: "conversations",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_labels_conversation_id_label",
                table: "conversation_labels",
                columns: new[] { "conversation_id", "label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversation_labels_tenant_id_conversation_id",
                table: "conversation_labels",
                columns: new[] { "tenant_id", "conversation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_connection_id_customer_channel_identity_id",
                table: "conversations",
                columns: new[] { "connection_id", "customer_channel_identity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_id_assigned_user_id",
                table: "conversations",
                columns: new[] { "tenant_id", "assigned_user_id" },
                filter: "assigned_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_id_customer_channel_identity_id",
                table: "conversations",
                columns: new[] { "tenant_id", "customer_channel_identity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_id_last_message_at",
                table: "conversations",
                columns: new[] { "tenant_id", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_id_status",
                table: "conversations",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_channel_identities_connection_id_external_user_id",
                table: "customer_channel_identities",
                columns: new[] { "connection_id", "external_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_channel_identities_tenant_id_customer_id",
                table: "customer_channel_identities",
                columns: new[] { "tenant_id", "customer_id" },
                filter: "customer_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_message_reactions_connection_id_provider_message_id_reactor",
                table: "message_reactions",
                columns: new[] { "connection_id", "provider_message_id", "reactor_channel_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_messages_connection_id_provider_message_id",
                table: "messages",
                columns: new[] { "connection_id", "provider_message_id" },
                unique: true,
                filter: "provider_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id_occurred_at_created_at_id",
                table: "messages",
                columns: new[] { "conversation_id", "occurred_at", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_id_conversation_id",
                table: "messages",
                columns: new[] { "tenant_id", "conversation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_id_inbound_event_id",
                table: "messages",
                columns: new[] { "tenant_id", "inbound_event_id" },
                filter: "inbound_event_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_labels");

            migrationBuilder.DropTable(
                name: "message_reactions");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropTable(
                name: "customer_channel_identities");
        }
    }
}
