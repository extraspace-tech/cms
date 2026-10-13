using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExtraSpace.Api.Platform.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropEntraObjectId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_staff_user_entra_object_id",
                table: "staff_user");

            migrationBuilder.DropColumn(
                name: "entra_object_id",
                table: "staff_user");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "entra_object_id",
                table: "staff_user",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_user_entra_object_id",
                table: "staff_user",
                column: "entra_object_id",
                unique: true);
        }
    }
}
