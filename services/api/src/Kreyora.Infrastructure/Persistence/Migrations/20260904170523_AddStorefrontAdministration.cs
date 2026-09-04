using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStorefrontAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "store_command_idempotency",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    resource_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_command_idempotency", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stores",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    platform_slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    normalized_platform_slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    tagline = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    theme_preset = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    brand_accent_hex = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    contact_name = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    contact_whats_app = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    facebook_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    instagram_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tik_tok_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    terms_policy = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    privacy_policy = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    returns_policy = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    payment_policy = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stores", x => x.id);
                    table.UniqueConstraint("ak_stores_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_stores_brand_accent", "brand_accent_hex IS NULL OR brand_accent_hex ~ '^#[0-9A-F]{6}$'");
                    table.CheckConstraint("ck_stores_platform_slug", "platform_slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                });

            migrationBuilder.CreateTable(
                name: "store_product_publications",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    store_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    product_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    visibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_product_publications", x => x.id);
                    table.ForeignKey(
                        name: "fk_store_product_publications_products_tenant_id_product_id",
                        columns: x => new { x.tenant_id, x.product_id },
                        principalTable: "products",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_store_product_publications_stores_tenant_id_store_id",
                        columns: x => new { x.tenant_id, x.store_id },
                        principalTable: "stores",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_store_command_idempotency_tenant_id_operation_idempotency_k",
                table: "store_command_idempotency",
                columns: new[] { "tenant_id", "operation", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_store_product_publications_store_id_product_id",
                table: "store_product_publications",
                columns: new[] { "store_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_store_product_publications_tenant_id_product_id_visibility",
                table: "store_product_publications",
                columns: new[] { "tenant_id", "product_id", "visibility" });

            migrationBuilder.CreateIndex(
                name: "ix_store_product_publications_tenant_id_store_id",
                table: "store_product_publications",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stores_normalized_platform_slug",
                table: "stores",
                column: "normalized_platform_slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stores_tenant_id",
                table: "stores",
                column: "tenant_id",
                unique: true,
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_stores_tenant_id_status",
                table: "stores",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "store_command_idempotency");

            migrationBuilder.DropTable(
                name: "store_product_publications");

            migrationBuilder.DropTable(
                name: "stores");
        }
    }
}
