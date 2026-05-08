using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelPro.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLitresAndRateToTesting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Litres",
                table: "TestingEntries",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "Rate",
                table: "TestingEntries",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            // These tables and indices are already created by legacy compatibility code in App.xaml.cs
            // migrationBuilder.CreateTable(
            //     name: "ShiftFuelRates",
            // ...
            // migrationBuilder.CreateIndex(
            //     name: "IX_ShiftOtherCash_ShiftId",
            //     table: "ShiftOtherCash",
            //     column: "ShiftId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShiftFuelRates");

            migrationBuilder.DropTable(
                name: "ShiftOtherCash");

            migrationBuilder.DropColumn(
                name: "Litres",
                table: "TestingEntries");

            migrationBuilder.DropColumn(
                name: "Rate",
                table: "TestingEntries");
        }
    }
}
