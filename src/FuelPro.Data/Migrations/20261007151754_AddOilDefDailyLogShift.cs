using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelPro.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOilDefDailyLogShift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ShiftId",
                table: "OilDefDailyLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OilDefDailyLogs_ShiftId",
                table: "OilDefDailyLogs",
                column: "ShiftId");

            migrationBuilder.AddForeignKey(
                name: "FK_OilDefDailyLogs_Shifts_ShiftId",
                table: "OilDefDailyLogs",
                column: "ShiftId",
                principalTable: "Shifts",
                principalColumn: "ShiftId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OilDefDailyLogs_Shifts_ShiftId",
                table: "OilDefDailyLogs");

            migrationBuilder.DropIndex(
                name: "IX_OilDefDailyLogs_ShiftId",
                table: "OilDefDailyLogs");

            migrationBuilder.DropColumn(
                name: "ShiftId",
                table: "OilDefDailyLogs");
        }
    }
}
