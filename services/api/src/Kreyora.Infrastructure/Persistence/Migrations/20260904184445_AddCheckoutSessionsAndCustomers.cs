using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutSessionsAndCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "actor_user_id",
                table: "inventory_reservations",
                type: "character varying(26)",
                maxLength: 26,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(26)",
                oldMaxLength: 26);

            migrationBuilder.CreateTable(
                name: "checkout_session_commands",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    checkout_session_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checkout_session_commands", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    phone = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    normalized_phone = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    privacy_acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    privacy_policy_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    retention_review_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_checkout_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                    table.UniqueConstraint("ak_customers_tenant_id_id", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "checkout_sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    store_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    quote_token_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quote_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    customer_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    customer_phone = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    customer_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    address_line1 = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    address_line2 = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    district = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    municipality = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    locality = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    landmark = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    privacy_policy_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    privacy_acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pii_review_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checkout_sessions", x => x.id);
                    table.UniqueConstraint("ak_checkout_sessions_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_checkout_sessions_terminal_timestamp", "(state = 'Active' AND completed_at IS NULL AND expired_at IS NULL AND cancelled_at IS NULL) OR (state = 'Completed' AND completed_at IS NOT NULL AND expired_at IS NULL AND cancelled_at IS NULL) OR (state = 'Expired' AND completed_at IS NULL AND expired_at IS NOT NULL AND cancelled_at IS NULL) OR (state = 'Cancelled' AND completed_at IS NULL AND expired_at IS NULL AND cancelled_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_checkout_sessions_customers_tenant_id_customer_id",
                        columns: x => new { x.tenant_id, x.customer_id },
                        principalTable: "customers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_checkout_sessions_delivery_rules_tenant_id_delivery_rule_id",
                        columns: x => new { x.tenant_id, x.delivery_rule_id },
                        principalTable: "delivery_rules",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_checkout_sessions_stores_tenant_id_store_id",
                        columns: x => new { x.tenant_id, x.store_id },
                        principalTable: "stores",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "checkout_session_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    checkout_session_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
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
                    table.PrimaryKey("pk_checkout_session_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_checkout_session_items_checkout_sessions_tenant_id_checkout",
                        columns: x => new { x.tenant_id, x.checkout_session_id },
                        principalTable: "checkout_sessions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_checkout_session_items_inventory_reservations_tenant_id_inv",
                        columns: x => new { x.tenant_id, x.inventory_reservation_id },
                        principalTable: "inventory_reservations",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_checkout_session_commands_tenant_id_operation_idempotency_k",
                table: "checkout_session_commands",
                columns: new[] { "tenant_id", "operation", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_checkout_session_items_tenant_id_checkout_session_id_varian",
                table: "checkout_session_items",
                columns: new[] { "tenant_id", "checkout_session_id", "variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_checkout_session_items_tenant_id_inventory_reservation_id",
                table: "checkout_session_items",
                columns: new[] { "tenant_id", "inventory_reservation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_checkout_sessions_tenant_id_customer_id",
                table: "checkout_sessions",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_checkout_sessions_tenant_id_delivery_rule_id",
                table: "checkout_sessions",
                columns: new[] { "tenant_id", "delivery_rule_id" });

            migrationBuilder.CreateIndex(
                name: "ix_checkout_sessions_tenant_id_expires_at_id",
                table: "checkout_sessions",
                columns: new[] { "tenant_id", "expires_at", "id" },
                filter: "state = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_checkout_sessions_tenant_id_store_id_quote_token_fingerprint",
                table: "checkout_sessions",
                columns: new[] { "tenant_id", "store_id", "quote_token_fingerprint" },
                unique: true,
                filter: "state = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_id_normalized_email",
                table: "customers",
                columns: new[] { "tenant_id", "normalized_email" },
                unique: true,
                filter: "normalized_email IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_id_normalized_phone",
                table: "customers",
                columns: new[] { "tenant_id", "normalized_phone" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checkout_session_commands");

            migrationBuilder.DropTable(
                name: "checkout_session_items");

            migrationBuilder.DropTable(
                name: "checkout_sessions");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.AlterColumn<string>(
                name: "actor_user_id",
                table: "inventory_reservations",
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
