using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ExtraSpace.Api.Platform.Data.Migrations
{
    /// <inheritdoc />
    public partial class Sites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "site",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sitelink_site_id = table.Column<int>(type: "integer", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    hero_image_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    address1 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tax_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    tax_label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    facility_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    stripe_account_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site", x => x.id);
                    table.CheckConstraint("ck_site_country", "country IN ('SG', 'MY', 'KR')");
                    table.CheckConstraint("ck_site_country_currency", "(country, currency) IN (('SG', 'SGD'), ('MY', 'MYR'), ('KR', 'KRW'))");
                    table.CheckConstraint("ck_site_currency", "currency IN ('SGD', 'MYR', 'KRW')");
                    table.CheckConstraint("ck_site_tax_percentage", "tax_percentage >= 0 AND tax_percentage <= 100");
                });

            migrationBuilder.CreateTable(
                name: "storage_type",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    icon_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    image_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_type", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "unit_sync_state",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_time_polled_ticks = table.Column<long>(type: "bigint", nullable: true),
                    last_synced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_sync_state", x => x.site_id);
                    table.ForeignKey(
                        name: "fk_unit_sync_state_site_site_id",
                        column: x => x.site_id,
                        principalTable: "site",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "unit_size",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    unit_group = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    image_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    min_area_sqft = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    max_area_sqft = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    storage_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_size", x => x.id);
                    table.CheckConstraint("ck_unit_size_range", "min_area_sqft IS NULL OR max_area_sqft IS NULL OR min_area_sqft <= max_area_sqft");
                    table.ForeignKey(
                        name: "fk_unit_size_storage_type_storage_type_id",
                        column: x => x.storage_type_id,
                        principalTable: "storage_type",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "unit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sitelink_unit_id = table.Column<int>(type: "integer", nullable: false),
                    sitelink_unit_type_id = table.Column<int>(type: "integer", nullable: false),
                    unit_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    width = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    length = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    area_sqft = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    floor = table.Column<int>(type: "integer", nullable: true),
                    climate = table.Column<bool>(type: "boolean", nullable: false),
                    inside = table.Column<bool>(type: "boolean", nullable: false),
                    power = table.Column<bool>(type: "boolean", nullable: false),
                    alarm = table.Column<bool>(type: "boolean", nullable: false),
                    ada = table.Column<bool>(type: "boolean", nullable: false),
                    mobile = table.Column<bool>(type: "boolean", nullable: false),
                    standard_rate = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    web_rate = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    board_rate = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    push_rate = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    preferred_rate = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    standard_security_deposit = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    rented = table.Column<bool>(type: "boolean", nullable: false),
                    rentable = table.Column<bool>(type: "boolean", nullable: false),
                    waiting_list_reserved = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    available = table.Column<bool>(type: "boolean", nullable: false, computedColumnSql: "rentable AND NOT rented AND NOT waiting_list_reserved AND deleted_at IS NULL", stored: true),
                    storage_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_size_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_synced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit", x => x.id);
                    table.ForeignKey(
                        name: "fk_unit_site_site_id",
                        column: x => x.site_id,
                        principalTable: "site",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_unit_storage_type_storage_type_id",
                        column: x => x.storage_type_id,
                        principalTable: "storage_type",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_unit_unit_size_unit_size_id",
                        column: x => x.unit_size_id,
                        principalTable: "unit_size",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "storage_type",
                columns: new[] { "id", "active", "created_at", "created_by_staff_id", "display_name", "icon_path", "image_path", "key", "name", "sort_order", "updated_at", "updated_by_staff_id" },
                values: new object[,]
                {
                    { new Guid("0199e000-0000-7000-8000-000000000001"), true, new DateTime(2026, 10, 14, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "self-storage", "Self Storage", 1, new DateTime(2026, 10, 14, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("0199e000-0000-7000-8000-000000000002"), false, new DateTime(2026, 10, 14, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "workspace", "Workspace", 2, new DateTime(2026, 10, 14, 0, 0, 0, 0, DateTimeKind.Utc), null }
                });

            migrationBuilder.CreateIndex(
                name: "ix_site_location_code",
                table: "site",
                column: "location_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_site_sitelink_site_id",
                table: "site",
                column: "sitelink_site_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_storage_type_key",
                table: "storage_type",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unit_site_id_sitelink_unit_id",
                table: "unit",
                columns: new[] { "site_id", "sitelink_unit_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unit_site_id_unit_name",
                table: "unit",
                columns: new[] { "site_id", "unit_name" });

            migrationBuilder.CreateIndex(
                name: "ix_unit_storage_type_id",
                table: "unit",
                column: "storage_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_unit_unit_size_id",
                table: "unit",
                column: "unit_size_id");

            migrationBuilder.CreateIndex(
                name: "ix_unit_size_key",
                table: "unit_size",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unit_size_storage_type_id",
                table: "unit_size",
                column: "storage_type_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "unit");

            migrationBuilder.DropTable(
                name: "unit_sync_state");

            migrationBuilder.DropTable(
                name: "unit_size");

            migrationBuilder.DropTable(
                name: "site");

            migrationBuilder.DropTable(
                name: "storage_type");
        }
    }
}
