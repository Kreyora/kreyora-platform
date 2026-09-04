using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCanonicalOrdersAndCommerceProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "actor_user_id",
                table: "stock_movements",
                type: "character varying(26)",
                maxLength: 26,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(26)",
                oldMaxLength: 26);

            migrationBuilder.AddColumn<string>(
                name: "actor_kind",
                table: "stock_movements",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Member");

            migrationBuilder.AlterColumn<string>(
                name: "actor_user_id",
                table: "audit_events",
                type: "character varying(26)",
                maxLength: 26,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(26)",
                oldMaxLength: 26);

            migrationBuilder.AddColumn<string>(
                name: "actor_kind",
                table: "audit_events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Member");

            migrationBuilder.CreateTable(
                name: "order_commands",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    order_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_commands", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    store_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    checkout_session_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    payment_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    fulfilment_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    customer_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    customer_phone = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    customer_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    address_line1 = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    address_line2 = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    district = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    municipality = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    locality = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    landmark = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    merchandise_subtotal_npr = table.Column<decimal>(type: "numeric", nullable: false),
                    discount_npr = table.Column<decimal>(type: "numeric", nullable: false),
                    delivery_fee_npr = table.Column<decimal>(type: "numeric", nullable: false),
                    tax_npr = table.Column<decimal>(type: "numeric", nullable: false),
                    provider_fee_npr = table.Column<decimal>(type: "numeric", nullable: false),
                    platform_fee_npr = table.Column<decimal>(type: "numeric", nullable: false),
                    total_npr = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    delivery_rule_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    delivery_rule_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    estimated_eta_text = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    cod_available = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.UniqueConstraint("ak_orders_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_orders_checkout_sessions_tenant_id_checkout_session_id",
                        columns: x => new { x.tenant_id, x.checkout_session_id },
                        principalTable: "checkout_sessions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_customers_tenant_id_customer_id",
                        columns: x => new { x.tenant_id, x.customer_id },
                        principalTable: "customers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_delivery_rules_tenant_id_delivery_rule_id",
                        columns: x => new { x.tenant_id, x.delivery_rule_id },
                        principalTable: "delivery_rules",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_stores_tenant_id_store_id",
                        columns: x => new { x.tenant_id, x.store_id },
                        principalTable: "stores",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    order_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    inventory_reservation_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    product_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    product_title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    variant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    variant_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price_npr = table.Column<decimal>(type: "numeric", nullable: false),
                    line_subtotal_npr = table.Column<decimal>(type: "numeric", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_items_inventory_reservations_tenant_id_inventory_rese",
                        columns: x => new { x.tenant_id, x.inventory_reservation_id },
                        principalTable: "inventory_reservations",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_order_items_orders_tenant_id_order_id",
                        columns: x => new { x.tenant_id, x.order_id },
                        principalTable: "orders",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_movements_actor_provenance",
                table: "stock_movements",
                sql: "(actor_kind = 'Member' AND actor_user_id IS NOT NULL) OR (actor_kind = 'CommerceSystem' AND actor_user_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_events_actor_provenance",
                table: "audit_events",
                sql: "(actor_kind = 'Member' AND actor_user_id IS NOT NULL) OR (actor_kind = 'CommerceSystem' AND actor_user_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_order_commands_tenant_id_operation_idempotency_key",
                table: "order_commands",
                columns: new[] { "tenant_id", "operation", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_items_tenant_id_inventory_reservation_id",
                table: "order_items",
                columns: new[] { "tenant_id", "inventory_reservation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_items_tenant_id_order_id_variant_id",
                table: "order_items",
                columns: new[] { "tenant_id", "order_id", "variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_checkout_session_id",
                table: "orders",
                columns: new[] { "tenant_id", "checkout_session_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_customer_id",
                table: "orders",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_delivery_rule_id",
                table: "orders",
                columns: new[] { "tenant_id", "delivery_rule_id" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_order_number",
                table: "orders",
                columns: new[] { "tenant_id", "order_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_status_created_at_id",
                table: "orders",
                columns: new[] { "tenant_id", "status", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_store_id",
                table: "orders",
                columns: new[] { "tenant_id", "store_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_commands");

            migrationBuilder.DropTable(
                name: "order_items");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_movements_actor_provenance",
                table: "stock_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_events_actor_provenance",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "actor_kind",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "actor_kind",
                table: "audit_events");

            migrationBuilder.AlterColumn<string>(
                name: "actor_user_id",
                table: "stock_movements",
                type: "character varying(26)",
                maxLength: 26,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(26)",
                oldMaxLength: 26,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "actor_user_id",
                table: "audit_events",
                type: "character varying(26)",
                maxLength: 26,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(26)",
                oldMaxLength: 26,
                oldNullable: true);
        }
    }
}
