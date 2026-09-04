using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "media_assets",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    product_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: true),
                    alt_text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    upload_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ready_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deletion_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_assets", x => x.id);
                    table.UniqueConstraint("ak_media_assets_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_media_assets_byte_size", "byte_size > 0");
                    table.CheckConstraint("ck_media_assets_sort_order", "sort_order IS NULL OR sort_order >= 0");
                    table.ForeignKey(
                        name: "fk_media_assets_products_tenant_id_product_id",
                        columns: x => new { x.tenant_id, x.product_id },
                        principalTable: "products",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_tenant_id_object_key",
                table: "media_assets",
                columns: new[] { "tenant_id", "object_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_tenant_id_product_id_sort_order",
                table: "media_assets",
                columns: new[] { "tenant_id", "product_id", "sort_order" },
                unique: true,
                filter: "product_id IS NOT NULL AND state <> 'Deleted'");

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_tenant_id_state_upload_expires_at",
                table: "media_assets",
                columns: new[] { "tenant_id", "state", "upload_expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_assets");
        }
    }
}
