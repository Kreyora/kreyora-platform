using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "channel_connections",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    store_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    credentials_ciphertext = table.Column<string>(type: "text", nullable: true),
                    credentials_iv = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    credentials_auth_tag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    credentials_key_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    capabilities = table.Column<string>(type: "jsonb", nullable: false),
                    token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    refresh_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_refreshed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_validated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_health_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    health_summary = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    health_details = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    webhook_verification_token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_connections", x => x.id);
                    table.UniqueConstraint("ak_channel_connections_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_channel_connections_stores_tenant_id_store_id",
                        columns: x => new { x.tenant_id, x.store_id },
                        principalTable: "stores",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_channel_connections_tenant_id_channel_external_account_id",
                table: "channel_connections",
                columns: new[] { "tenant_id", "channel", "external_account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_channel_connections_tenant_id_status",
                table: "channel_connections",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_channel_connections_tenant_id_store_id",
                table: "channel_connections",
                columns: new[] { "tenant_id", "store_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channel_connections");
        }
    }
}
