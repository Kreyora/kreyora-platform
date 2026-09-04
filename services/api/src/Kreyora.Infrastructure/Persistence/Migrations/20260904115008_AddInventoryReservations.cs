using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reference_id",
                table: "stock_movements",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reference_type",
                table: "stock_movements",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inventory_reservations",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    inventory_item_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    variant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reference_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    actor_user_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    committed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_reservations", x => x.id);
                    table.UniqueConstraint("ak_inventory_reservations_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_inventory_reservations_quantity_positive", "quantity > 0");
                    table.CheckConstraint("ck_inventory_reservations_terminal_timestamp", "(state = 'Active' AND committed_at IS NULL AND released_at IS NULL AND expired_at IS NULL) OR (state = 'Committed' AND committed_at IS NOT NULL AND released_at IS NULL AND expired_at IS NULL) OR (state = 'Released' AND committed_at IS NULL AND released_at IS NOT NULL AND expired_at IS NULL) OR (state = 'Expired' AND committed_at IS NULL AND released_at IS NULL AND expired_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_inventory_reservations_inventory_items_tenant_id_inventory_",
                        columns: x => new { x.tenant_id, x.inventory_item_id },
                        principalTable: "inventory_items",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_reservations_product_variants_tenant_id_variant_id",
                        columns: x => new { x.tenant_id, x.variant_id },
                        principalTable: "product_variants",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_reservation_commands",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    reservation_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_reservation_commands", x => x.id);
                    table.ForeignKey(
                        name: "fk_inventory_reservation_commands_inventory_reservations_tenan",
                        columns: x => new { x.tenant_id, x.reservation_id },
                        principalTable: "inventory_reservations",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reservation_commands_tenant_id_operation_idempote",
                table: "inventory_reservation_commands",
                columns: new[] { "tenant_id", "operation", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reservation_commands_tenant_id_reservation_id",
                table: "inventory_reservation_commands",
                columns: new[] { "tenant_id", "reservation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reservations_tenant_id_expires_at_id",
                table: "inventory_reservations",
                columns: new[] { "tenant_id", "expires_at", "id" },
                filter: "state = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reservations_tenant_id_inventory_item_id_state_ex",
                table: "inventory_reservations",
                columns: new[] { "tenant_id", "inventory_item_id", "state", "expires_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reservations_tenant_id_variant_id",
                table: "inventory_reservations",
                columns: new[] { "tenant_id", "variant_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_reservation_commands");

            migrationBuilder.DropTable(
                name: "inventory_reservations");

            migrationBuilder.DropColumn(
                name: "reference_id",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "reference_type",
                table: "stock_movements");
        }
    }
}
