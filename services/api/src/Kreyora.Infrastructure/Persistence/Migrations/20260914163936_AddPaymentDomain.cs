using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_attempts",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    order_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount_npr = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    internal_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verified_by_user_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    rejected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejected_by_user_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    collected_by_user_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_attempts", x => x.id);
                    table.UniqueConstraint("ak_payment_attempts_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_payment_attempts_amount_npr", "amount_npr >= 0");
                    table.ForeignKey(
                        name: "fk_payment_attempts_orders_tenant_id_order_id",
                        columns: x => new { x.tenant_id, x.order_id },
                        principalTable: "orders",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "store_payment_configurations",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    store_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    cod_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    merchant_qr_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    merchant_qr_instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    merchant_qr_media_asset_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_payment_configurations", x => x.id);
                    table.UniqueConstraint("ak_store_payment_configurations_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_store_payment_configurations_at_least_one", "cod_enabled = TRUE OR merchant_qr_enabled = TRUE");
                    table.ForeignKey(
                        name: "fk_store_payment_configurations_media_assets_tenant_id_merchan",
                        columns: x => new { x.tenant_id, x.merchant_qr_media_asset_id },
                        principalTable: "media_assets",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_store_payment_configurations_stores_tenant_id_store_id",
                        columns: x => new { x.tenant_id, x.store_id },
                        principalTable: "stores",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_proofs",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    payment_attempt_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    customer_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    upload_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ready_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deletion_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_proofs", x => x.id);
                    table.UniqueConstraint("ak_payment_proofs_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_payment_proofs_byte_size", "byte_size > 0");
                    table.ForeignKey(
                        name: "fk_payment_proofs_payment_attempts_tenant_id_payment_attempt_id",
                        columns: x => new { x.tenant_id, x.payment_attempt_id },
                        principalTable: "payment_attempts",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_tenant_id_internal_reference",
                table: "payment_attempts",
                columns: new[] { "tenant_id", "internal_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_tenant_id_order_id",
                table: "payment_attempts",
                columns: new[] { "tenant_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_tenant_id_status",
                table: "payment_attempts",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_proofs_tenant_id_object_key",
                table: "payment_proofs",
                columns: new[] { "tenant_id", "object_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_proofs_tenant_id_payment_attempt_id",
                table: "payment_proofs",
                columns: new[] { "tenant_id", "payment_attempt_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_proofs_tenant_id_status_upload_expires_at",
                table: "payment_proofs",
                columns: new[] { "tenant_id", "status", "upload_expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_store_payment_configurations_tenant_id_merchant_qr_media_as",
                table: "store_payment_configurations",
                columns: new[] { "tenant_id", "merchant_qr_media_asset_id" });

            migrationBuilder.CreateIndex(
                name: "ix_store_payment_configurations_tenant_id_store_id",
                table: "store_payment_configurations",
                columns: new[] { "tenant_id", "store_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_proofs");

            migrationBuilder.DropTable(
                name: "store_payment_configurations");

            migrationBuilder.DropTable(
                name: "payment_attempts");
        }
    }
}
