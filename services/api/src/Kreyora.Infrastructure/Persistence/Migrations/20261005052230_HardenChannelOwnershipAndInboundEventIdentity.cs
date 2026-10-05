using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenChannelOwnershipAndInboundEventIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-015 guard: global account ownership cannot be applied while one external account is
            // connected in several tenants. Reports a count only (no identifiers) and aborts the migration.
            migrationBuilder.Sql("""
                DO $$
                DECLARE duplicate_accounts integer;
                BEGIN
                    SELECT count(*) INTO duplicate_accounts FROM (
                        SELECT channel, external_account_id
                        FROM channel_connections
                        GROUP BY channel, external_account_id
                        HAVING count(*) > 1) AS duplicates;
                    IF duplicate_accounts > 0 THEN
                        RAISE EXCEPTION 'Migration blocked: % external account(s) are connected in more than one tenant. Resolve ownership before applying (ADR-015).', duplicate_accounts;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "ix_inbound_events_connection_id_provider_message_id",
                table: "inbound_events");

            migrationBuilder.DropIndex(
                name: "ix_channel_connections_tenant_id_channel_external_account_id",
                table: "channel_connections");

            migrationBuilder.AlterColumn<string>(
                name: "provider_message_id",
                table: "inbound_events",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deduplication_key",
                table: "inbound_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Backfill with the M07 legacy identity, hashed exactly like InboundEvent.HashIdentity
            // (lowercase hex SHA-256 of UTF-8): the provider message ID, else the row ID (no dedup).
            migrationBuilder.Sql("""
                UPDATE inbound_events
                SET deduplication_key = encode(sha256(convert_to(COALESCE(provider_message_id, id), 'UTF8')), 'hex');
                """);

            migrationBuilder.CreateIndex(
                name: "ix_inbound_events_connection_id_deduplication_key",
                table: "inbound_events",
                columns: new[] { "connection_id", "deduplication_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbound_events_connection_id_provider_message_id",
                table: "inbound_events",
                columns: new[] { "connection_id", "provider_message_id" },
                filter: "provider_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_channel_connections_channel_external_account_id",
                table: "channel_connections",
                columns: new[] { "channel", "external_account_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restoring the old unique provider-message index fails once several inbound events reference
            // one message (read receipts/reactions) or a message ID exceeds 128 characters. Forward-fix instead.
            migrationBuilder.DropIndex(
                name: "ix_inbound_events_connection_id_deduplication_key",
                table: "inbound_events");

            migrationBuilder.DropIndex(
                name: "ix_inbound_events_connection_id_provider_message_id",
                table: "inbound_events");

            migrationBuilder.DropIndex(
                name: "ix_channel_connections_channel_external_account_id",
                table: "channel_connections");

            migrationBuilder.DropColumn(
                name: "deduplication_key",
                table: "inbound_events");

            migrationBuilder.AlterColumn<string>(
                name: "provider_message_id",
                table: "inbound_events",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbound_events_connection_id_provider_message_id",
                table: "inbound_events",
                columns: new[] { "connection_id", "provider_message_id" },
                unique: true,
                filter: "provider_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_channel_connections_tenant_id_channel_external_account_id",
                table: "channel_connections",
                columns: new[] { "tenant_id", "channel", "external_account_id" },
                unique: true);
        }
    }
}
