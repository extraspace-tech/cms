using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExtraSpace.Api.Platform.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_login_code",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    request_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    last_sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    window_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sent_in_window = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_login_code", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_refresh_token",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    user_agent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_refresh_token", x => x.id);
                    table.ForeignKey(
                        name: "fk_customer_refresh_token_customer_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_email",
                table: "customer",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_login_code_email",
                table: "customer_login_code",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_login_code_expires_at",
                table: "customer_login_code",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_customer_refresh_token_customer_id",
                table: "customer_refresh_token",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_refresh_token_expires_at",
                table: "customer_refresh_token",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_customer_refresh_token_family_id",
                table: "customer_refresh_token",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_refresh_token_token_hash",
                table: "customer_refresh_token",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_login_code");

            migrationBuilder.DropTable(
                name: "customer_refresh_token");

            migrationBuilder.DropTable(
                name: "customer");
        }
    }
}
