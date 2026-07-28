using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelPro.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddKhandharePetroleumEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DsmSalaryHistories",
                columns: table => new
                {
                    DsmSalaryHistoryId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DsmProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    OldBaseSalary = table.Column<double>(type: "REAL", nullable: false),
                    NewBaseSalary = table.Column<double>(type: "REAL", nullable: false),
                    OldSalaryType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    NewSalaryType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ChangeDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DsmSalaryHistories", x => x.DsmSalaryHistoryId);
                    table.ForeignKey(
                        name: "FK_DsmSalaryHistories_DsmProfiles_DsmProfileId",
                        column: x => x.DsmProfileId,
                        principalTable: "DsmProfiles",
                        principalColumn: "DsmProfileId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DsmSalaryPayments",
                columns: table => new
                {
                    DsmSalaryPaymentId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DsmProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    NetSalary = table.Column<double>(type: "REAL", nullable: false),
                    PaidAmount = table.Column<double>(type: "REAL", nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PaymentMode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Remarks = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DsmSalaryPayments", x => x.DsmSalaryPaymentId);
                    table.ForeignKey(
                        name: "FK_DsmSalaryPayments_DsmProfiles_DsmProfileId",
                        column: x => x.DsmProfileId,
                        principalTable: "DsmProfiles",
                        principalColumn: "DsmProfileId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FuelTankers",
                columns: table => new
                {
                    FuelTankerId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TankerDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TankerNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    InvoiceNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FuelType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Quantity = table.Column<double>(type: "REAL", nullable: false),
                    PurchaseRate = table.Column<double>(type: "REAL", nullable: false),
                    TotalAmount = table.Column<double>(type: "REAL", nullable: false),
                    Density = table.Column<double>(type: "REAL", nullable: false),
                    Remarks = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FuelTankers", x => x.FuelTankerId);
                });

            migrationBuilder.CreateTable(
                name: "KhandharePetroleumEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DsmEntryId = table.Column<int>(type: "INTEGER", nullable: true),
                    DsmName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SlipNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Amount = table.Column<double>(type: "REAL", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SyncGuid = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KhandharePetroleumEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KhandharePetroleumEntries_DsmEntries_DsmEntryId",
                        column: x => x.DsmEntryId,
                        principalTable: "DsmEntries",
                        principalColumn: "DsmEntryId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TankDailyStocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FuelType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    OpeningStock = table.Column<double>(type: "REAL", nullable: false),
                    DaySaleLitres = table.Column<double>(type: "REAL", nullable: false),
                    TestingLitres = table.Column<double>(type: "REAL", nullable: false),
                    PurchasedLitres = table.Column<double>(type: "REAL", nullable: false),
                    ClosingStock = table.Column<double>(type: "REAL", nullable: false),
                    DipMm = table.Column<double>(type: "REAL", nullable: false),
                    ManualStock = table.Column<double>(type: "REAL", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TankDailyStocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TankDailyStocks_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "ShiftId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DsmSalaryHistories_DsmProfileId",
                table: "DsmSalaryHistories",
                column: "DsmProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_DsmSalaryPayments_DsmProfileId",
                table: "DsmSalaryPayments",
                column: "DsmProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_KhandharePetroleumEntries_DsmEntryId",
                table: "KhandharePetroleumEntries",
                column: "DsmEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_KhandharePetroleumEntries_DsmName_Date",
                table: "KhandharePetroleumEntries",
                columns: new[] { "DsmName", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_KhandharePetroleumEntries_SyncGuid",
                table: "KhandharePetroleumEntries",
                column: "SyncGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TankDailyStocks_ShiftId",
                table: "TankDailyStocks",
                column: "ShiftId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DsmSalaryHistories");

            migrationBuilder.DropTable(
                name: "DsmSalaryPayments");

            migrationBuilder.DropTable(
                name: "FuelTankers");

            migrationBuilder.DropTable(
                name: "KhandharePetroleumEntries");

            migrationBuilder.DropTable(
                name: "TankDailyStocks");
        }
    }
}
