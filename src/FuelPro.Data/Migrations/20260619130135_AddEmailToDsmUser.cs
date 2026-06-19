using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelPro.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailToDsmUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "DsmUsers",
                type: "TEXT",
                maxLength: 255,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Email",
                table: "DsmUsers");
        }
    }
}
