using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryLedgerAndBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_product_variants_tenant_id_id",
                table: "product_variants",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateTable(
                name: "inventory_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    variant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    on_hand_quantity = table.Column<int>(type: "integer", nullable: false),
                    reserved_quantity = table.Column<int>(type: "integer", nullable: false),
                    low_stock_threshold = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_items", x => x.id);
                    table.UniqueConstraint("ak_inventory_items_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_inventory_items_low_stock_threshold_non_negative", "low_stock_threshold >= 0");
                    table.CheckConstraint("ck_inventory_items_on_hand_non_negative", "on_hand_quantity >= 0");
                    table.CheckConstraint("ck_inventory_items_reserved_non_negative", "reserved_quantity >= 0");
                    table.CheckConstraint("ck_inventory_items_reserved_not_above_on_hand", "reserved_quantity <= on_hand_quantity");
                    table.ForeignKey(
                        name: "fk_inventory_items_product_variants_tenant_id_variant_id",
                        columns: x => new { x.tenant_id, x.variant_id },
                        principalTable: "product_variants",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    inventory_item_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    variant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    quantity_delta = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    actor_user_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.CheckConstraint("ck_stock_movements_quantity_non_zero", "quantity_delta <> 0");
                    table.ForeignKey(
                        name: "fk_stock_movements_inventory_items_tenant_id_inventory_item_id",
                        columns: x => new { x.tenant_id, x.inventory_item_id },
                        principalTable: "inventory_items",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_product_variants_tenant_id_variant_id",
                        columns: x => new { x.tenant_id, x.variant_id },
                        principalTable: "product_variants",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_tenant_id_low_stock_threshold_modified_at",
                table: "inventory_items",
                columns: new[] { "tenant_id", "low_stock_threshold", "modified_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_tenant_id_variant_id",
                table: "inventory_items",
                columns: new[] { "tenant_id", "variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_idempotency_key",
                table: "stock_movements",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_inventory_item_id_created_at_id",
                table: "stock_movements",
                columns: new[] { "tenant_id", "inventory_item_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_variant_id",
                table: "stock_movements",
                columns: new[] { "tenant_id", "variant_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "inventory_items");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_product_variants_tenant_id_id",
                table: "product_variants");
        }
    }
}
