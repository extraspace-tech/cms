using System;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExtraSpace.Api.Platform.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdminTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    permissions = table.Column<string[]>(type: "text[]", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role", x => x.id);
                });

            migrationBuilder.Sql("CREATE UNIQUE INDEX ix_role_name_lower ON role (lower(name));");

            migrationBuilder.CreateTable(
                name: "staff_user",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    entra_object_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    last_sign_in_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_user", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_user_role_role_id",
                        column: x => x.role_id,
                        principalTable: "role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changes = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    ip = table.Column<IPAddress>(type: "inet", nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_log_staff_user_staff_user_id",
                        column: x => x.staff_user_id,
                        principalTable: "staff_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "role",
                columns: new[] { "id", "created_at", "created_by_staff_id", "is_system", "name", "permissions", "updated_at", "updated_by_staff_id" },
                values: new object[] { new Guid("0199dd00-0000-7000-8000-000000000001"), new DateTime(2026, 10, 13, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Owner", new string[0], new DateTime(2026, 10, 13, 0, 0, 0, 0, DateTimeKind.Utc), null });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entity_type_entity_id",
                table: "audit_log",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_occurred_at",
                table: "audit_log",
                column: "occurred_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_staff_user_id",
                table: "audit_log",
                column: "staff_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_user_email",
                table: "staff_user",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_user_entra_object_id",
                table: "staff_user",
                column: "entra_object_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_user_role_id",
                table: "staff_user",
                column: "role_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "staff_user");

            migrationBuilder.Sql("DROP INDEX ix_role_name_lower;");

            migrationBuilder.DropTable(
                name: "role");
        }
    }
}
