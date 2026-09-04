using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kreyora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryRulesAndQuoteEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "delivery_rules",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    store_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    fee_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    base_fee_npr = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    free_above_npr = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    estimated_eta_text = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    cod_available = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_rules", x => x.id);
                    table.UniqueConstraint("ak_delivery_rules_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_delivery_rules_base_fee", "base_fee_npr >= 0");
                    table.CheckConstraint("ck_delivery_rules_priority", "priority >= 0 AND priority <= 10000");
                    table.CheckConstraint("ck_delivery_rules_threshold", "(fee_type = 'Threshold' AND free_above_npr > 0) OR (fee_type = 'Flat' AND free_above_npr IS NULL)");
                    table.ForeignKey(
                        name: "fk_delivery_rules_stores_tenant_id_store_id",
                        columns: x => new { x.tenant_id, x.store_id },
                        principalTable: "stores",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_rule_zones",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    delivery_rule_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    district = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    normalized_district = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    municipality = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    normalized_municipality = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    locality = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    normalized_locality = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_rule_zones", x => x.id);
                    table.ForeignKey(
                        name: "fk_delivery_rule_zones_delivery_rules_tenant_id_delivery_rule_",
                        columns: x => new { x.tenant_id, x.delivery_rule_id },
                        principalTable: "delivery_rules",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_rule_zones_delivery_rule_id_normalized_district_no",
                table: "delivery_rule_zones",
                columns: new[] { "delivery_rule_id", "normalized_district", "normalized_municipality", "normalized_locality" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_rule_zones_tenant_id_delivery_rule_id",
                table: "delivery_rule_zones",
                columns: new[] { "tenant_id", "delivery_rule_id" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_rule_zones_tenant_id_normalized_district_normalize",
                table: "delivery_rule_zones",
                columns: new[] { "tenant_id", "normalized_district", "normalized_municipality", "normalized_locality" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_rules_tenant_id_store_id_is_active_priority",
                table: "delivery_rules",
                columns: new[] { "tenant_id", "store_id", "is_active", "priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_rule_zones");

            migrationBuilder.DropTable(
                name: "delivery_rules");
        }
    }
}
