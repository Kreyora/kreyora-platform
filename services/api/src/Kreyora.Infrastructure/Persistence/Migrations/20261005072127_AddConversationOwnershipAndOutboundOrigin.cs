using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationOwnershipAndOutboundOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "actor_user_id",
                table: "outbound_messages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "outbound_messages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "System");

            migrationBuilder.AddColumn<string>(
                name: "actor_user_id",
                table: "messages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "outbound_message_id",
                table: "messages",
                type: "character varying(26)",
                maxLength: 26,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_messages_tenant_id_conversation_id_origin_status",
                table: "outbound_messages",
                columns: new[] { "tenant_id", "conversation_id", "origin", "status" },
                filter: "conversation_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_messages_outbound_message_id",
                table: "messages",
                column: "outbound_message_id",
                unique: true,
                filter: "outbound_message_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbound_messages_tenant_id_conversation_id_origin_status",
                table: "outbound_messages");

            migrationBuilder.DropIndex(
                name: "ix_messages_outbound_message_id",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "actor_user_id",
                table: "outbound_messages");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "outbound_messages");

            migrationBuilder.DropColumn(
                name: "actor_user_id",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "outbound_message_id",
                table: "messages");
        }
    }
}
