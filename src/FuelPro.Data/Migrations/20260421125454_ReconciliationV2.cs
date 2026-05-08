using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelPro.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReconciliationV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MsRate",
                table: "Settings");

            migrationBuilder.AlterColumn<double>(
                name: "HsdRate",
                table: "Settings",
                type: "REAL",
                nullable: false,
                defaultValue: 90.349999999999994,
                oldClrType: typeof(double),
                oldType: "REAL",
                oldDefaultValue: 89.620000000000005);

            migrationBuilder.AddColumn<double>(
                name: "MsIIRate",
                table: "Settings",
                type: "REAL",
                nullable: false,
                defaultValue: 103.81);

            migrationBuilder.AddColumn<double>(
                name: "MsIRate",
                table: "Settings",
                type: "REAL",
                nullable: false,
                defaultValue: 103.81);

            migrationBuilder.AddColumn<bool>(
                name: "IsManualOpeningOverride",
                table: "NozzleReadings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossSales",
                table: "DsmEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Mismatch",
                table: "DsmEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalCollection",
                table: "DsmEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalCreditors",
                table: "DsmEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalInDirect",
                table: "DsmEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "AppMeta",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppMeta", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppMeta");

            migrationBuilder.DropColumn(
                name: "MsIIRate",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "MsIRate",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "IsManualOpeningOverride",
                table: "NozzleReadings");

            migrationBuilder.DropColumn(
                name: "GrossSales",
                table: "DsmEntries");

            migrationBuilder.DropColumn(
                name: "Mismatch",
                table: "DsmEntries");

            migrationBuilder.DropColumn(
                name: "TotalCollection",
                table: "DsmEntries");

            migrationBuilder.DropColumn(
                name: "TotalCreditors",
                table: "DsmEntries");

            migrationBuilder.DropColumn(
                name: "TotalInDirect",
                table: "DsmEntries");

            migrationBuilder.AlterColumn<double>(
                name: "HsdRate",
                table: "Settings",
                type: "REAL",
                nullable: false,
                defaultValue: 89.620000000000005,
                oldClrType: typeof(double),
                oldType: "REAL",
                oldDefaultValue: 90.349999999999994);

            migrationBuilder.AddColumn<double>(
                name: "MsRate",
                table: "Settings",
                type: "REAL",
                nullable: false,
                defaultValue: 94.719999999999999);
        }
    }
}
