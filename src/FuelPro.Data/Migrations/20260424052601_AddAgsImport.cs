using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelPro.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgsImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgsDailySummaries",
                columns: table => new
                {
                    AgsDailySummaryId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SummaryDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DayTotalHsdLitres = table.Column<double>(type: "REAL", nullable: false),
                    DayTotalMsILitres = table.Column<double>(type: "REAL", nullable: false),
                    DayTotalMsIILitres = table.Column<double>(type: "REAL", nullable: false),
                    HsdDayOpeningStock = table.Column<double>(type: "REAL", nullable: false),
                    HsdDayClosingStock = table.Column<double>(type: "REAL", nullable: false),
                    MsIDayOpeningStock = table.Column<double>(type: "REAL", nullable: false),
                    MsIDayClosingStock = table.Column<double>(type: "REAL", nullable: false),
                    MsIIDayOpeningStock = table.Column<double>(type: "REAL", nullable: false),
                    MsIIDayClosingStock = table.Column<double>(type: "REAL", nullable: false),
                    ShiftAImported = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShiftBImported = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShiftCImported = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NozzleDaySalesJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    ShiftBreakdownJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgsDailySummaries", x => x.AgsDailySummaryId);
                });

            migrationBuilder.CreateTable(
                name: "AgsShiftImports",
                columns: table => new
                {
                    AgsShiftImportId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ImportDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ShiftType = table.Column<string>(type: "TEXT", maxLength: 1, nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PdfFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ImportedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    TotalHsdLitres = table.Column<double>(type: "REAL", nullable: false),
                    TotalMsILitres = table.Column<double>(type: "REAL", nullable: false),
                    TotalMsIILitres = table.Column<double>(type: "REAL", nullable: false),
                    HsdOpeningStock = table.Column<double>(type: "REAL", nullable: false),
                    HsdClosingStock = table.Column<double>(type: "REAL", nullable: false),
                    MsIOpeningStock = table.Column<double>(type: "REAL", nullable: false),
                    MsIClosingStock = table.Column<double>(type: "REAL", nullable: false),
                    MsIIOpeningStock = table.Column<double>(type: "REAL", nullable: false),
                    MsIIClosingStock = table.Column<double>(type: "REAL", nullable: false),
                    PdfPeriodFrom = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    PdfPeriodTo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgsShiftImports", x => x.AgsShiftImportId);
                });

            migrationBuilder.CreateTable(
                name: "AgsNozzleReadings",
                columns: table => new
                {
                    AgsNozzleReadingId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AgsShiftImportId = table.Column<int>(type: "INTEGER", nullable: false),
                    NozzleNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    FuelType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    PumpNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    OpeningReading = table.Column<double>(type: "REAL", nullable: false),
                    ClosingReading = table.Column<double>(type: "REAL", nullable: false),
                    SaleLitres = table.Column<double>(type: "REAL", nullable: false),
                    TestingDeduction = table.Column<double>(type: "REAL", nullable: false),
                    NetSaleLitres = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgsNozzleReadings", x => x.AgsNozzleReadingId);
                    table.ForeignKey(
                        name: "FK_AgsNozzleReadings_AgsShiftImports_AgsShiftImportId",
                        column: x => x.AgsShiftImportId,
                        principalTable: "AgsShiftImports",
                        principalColumn: "AgsShiftImportId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgsTankStocks",
                columns: table => new
                {
                    AgsTankStockId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AgsShiftImportId = table.Column<int>(type: "INTEGER", nullable: false),
                    TankNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    FuelType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    OpeningDipMM = table.Column<double>(type: "REAL", nullable: false),
                    ClosingDipMM = table.Column<double>(type: "REAL", nullable: false),
                    OpeningStockLitres = table.Column<double>(type: "REAL", nullable: false),
                    ClosingStockLitres = table.Column<double>(type: "REAL", nullable: false),
                    FuelDispensedLitres = table.Column<double>(type: "REAL", nullable: false),
                    ReceiptLitres = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgsTankStocks", x => x.AgsTankStockId);
                    table.ForeignKey(
                        name: "FK_AgsTankStocks_AgsShiftImports_AgsShiftImportId",
                        column: x => x.AgsShiftImportId,
                        principalTable: "AgsShiftImports",
                        principalColumn: "AgsShiftImportId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgsDailySummaries_SummaryDate",
                table: "AgsDailySummaries",
                column: "SummaryDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgsNozzleReadings_AgsShiftImportId",
                table: "AgsNozzleReadings",
                column: "AgsShiftImportId");

            migrationBuilder.CreateIndex(
                name: "IX_AgsShiftImports_ImportDate_ShiftType_IsActive",
                table: "AgsShiftImports",
                columns: new[] { "ImportDate", "ShiftType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AgsTankStocks_AgsShiftImportId",
                table: "AgsTankStocks",
                column: "AgsShiftImportId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgsDailySummaries");

            migrationBuilder.DropTable(
                name: "AgsNozzleReadings");

            migrationBuilder.DropTable(
                name: "AgsTankStocks");

            migrationBuilder.DropTable(
                name: "AgsShiftImports");
        }
    }
}
