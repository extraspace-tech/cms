using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExtraSpace.Api.Platform.Data.Migrations
{
    /// <inheritdoc />
    public partial class StaffEmailCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Written by hand: EF scaffolds a drop + create for the renamed entity, which would lose the rows.
            migrationBuilder.RenameTable(
                name: "customer_login_code",
                newName: "login_code");

            migrationBuilder.Sql("ALTER TABLE login_code RENAME CONSTRAINT pk_customer_login_code TO pk_login_code;");

            migrationBuilder.RenameIndex(
                name: "ix_customer_login_code_expires_at",
                table: "login_code",
                newName: "ix_login_code_expires_at");

            migrationBuilder.DropIndex(
                name: "ix_customer_login_code_email",
                table: "login_code");

            // Existing rows are customer codes. The default is dropped again: every insert names its purpose.
            migrationBuilder.AddColumn<string>(
                name: "purpose",
                table: "login_code",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Customer");

            migrationBuilder.Sql("ALTER TABLE login_code ALTER COLUMN purpose DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "ix_login_code_purpose_email",
                table: "login_code",
                columns: new[] { "purpose", "email" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_login_code_purpose_email",
                table: "login_code");

            // The old table holds one row per email: staff codes cannot go back.
            migrationBuilder.Sql("DELETE FROM login_code WHERE purpose <> 'Customer';");

            migrationBuilder.DropColumn(
                name: "purpose",
                table: "login_code");

            migrationBuilder.RenameIndex(
                name: "ix_login_code_expires_at",
                table: "login_code",
                newName: "ix_customer_login_code_expires_at");

            migrationBuilder.Sql("ALTER TABLE login_code RENAME CONSTRAINT pk_login_code TO pk_customer_login_code;");

            migrationBuilder.RenameTable(
                name: "login_code",
                newName: "customer_login_code");

            migrationBuilder.CreateIndex(
                name: "ix_customer_login_code_email",
                table: "customer_login_code",
                column: "email",
                unique: true);
        }
    }
}
