using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelPro.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    SettingId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HsdRate = table.Column<double>(type: "REAL", nullable: false, defaultValue: 89.620000000000005),
                    MsRate = table.Column<double>(type: "REAL", nullable: false, defaultValue: 94.719999999999999),
                    PumpStationName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false, defaultValue: "VKD Petroleum"),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.SettingId);
                });

            migrationBuilder.CreateTable(
                name: "Shifts",
                columns: table => new
                {
                    ShiftId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShiftDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ShiftType = table.Column<string>(type: "TEXT", maxLength: 1, nullable: false),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shifts", x => x.ShiftId);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PinHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "Operator"),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    MustChangePin = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "DsmEntries",
                columns: table => new
                {
                    DsmEntryId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShiftId = table.Column<int>(type: "INTEGER", nullable: false),
                    DsmName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PumpId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DsmEntries", x => x.DsmEntryId);
                    table.ForeignKey(
                        name: "FK_DsmEntries_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "ShiftId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CashDenominations",
                columns: table => new
                {
                    CashDenomId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DsmEntryId = table.Column<int>(type: "INTEGER", nullable: false),
                    CashType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Denom500 = table.Column<int>(type: "INTEGER", nullable: false),
                    Denom200 = table.Column<int>(type: "INTEGER", nullable: false),
                    Denom100 = table.Column<int>(type: "INTEGER", nullable: false),
                    Denom50 = table.Column<int>(type: "INTEGER", nullable: false),
                    Denom20 = table.Column<int>(type: "INTEGER", nullable: false),
                    Denom10 = table.Column<int>(type: "INTEGER", nullable: false),
                    Coins = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalAmount = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashDenominations", x => x.CashDenomId);
                    table.ForeignKey(
                        name: "FK_CashDenominations_DsmEntries_DsmEntryId",
                        column: x => x.DsmEntryId,
                        principalTable: "DsmEntries",
                        principalColumn: "DsmEntryId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DebitEntries",
                columns: table => new
                {
                    DebitId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DsmEntryId = table.Column<int>(type: "INTEGER", nullable: false),
                    DebtorName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Amount = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebitEntries", x => x.DebitId);
                    table.ForeignKey(
                        name: "FK_DebitEntries_DsmEntries_DsmEntryId",
                        column: x => x.DsmEntryId,
                        principalTable: "DsmEntries",
                        principalColumn: "DsmEntryId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    ExpenseId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DsmEntryId = table.Column<int>(type: "INTEGER", nullable: true),
                    ShiftId = table.Column<int>(type: "INTEGER", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Amount = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.ExpenseId);
                    table.ForeignKey(
                        name: "FK_Expenses_DsmEntries_DsmEntryId",
                        column: x => x.DsmEntryId,
                        principalTable: "DsmEntries",
                        principalColumn: "DsmEntryId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Expenses_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "ShiftId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "NozzleReadings",
                columns: table => new
                {
                    NozzleReadingId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DsmEntryId = table.Column<int>(type: "INTEGER", nullable: false),
                    NozzleNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    FuelType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    OpeningReading = table.Column<double>(type: "REAL", nullable: false),
                    ClosingReading = table.Column<double>(type: "REAL", nullable: false),
                    SaleLitres = table.Column<double>(type: "REAL", nullable: false),
                    Rate = table.Column<double>(type: "REAL", nullable: false),
                    Amount = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NozzleReadings", x => x.NozzleReadingId);
                    table.ForeignKey(
                        name: "FK_NozzleReadings_DsmEntries_DsmEntryId",
                        column: x => x.DsmEntryId,
                        principalTable: "DsmEntries",
                        principalColumn: "DsmEntryId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentCollections",
                columns: table => new
                {
                    PaymentId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DsmEntryId = table.Column<int>(type: "INTEGER", nullable: false),
                    PhonePeCard = table.Column<double>(type: "REAL", nullable: false, defaultValue: 0.0),
                    PhonePe = table.Column<double>(type: "REAL", nullable: false, defaultValue: 0.0),
                    CreditCard = table.Column<double>(type: "REAL", nullable: false, defaultValue: 0.0),
                    PetroCard = table.Column<double>(type: "REAL", nullable: false, defaultValue: 0.0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentCollections", x => x.PaymentId);
                    table.ForeignKey(
                        name: "FK_PaymentCollections_DsmEntries_DsmEntryId",
                        column: x => x.DsmEntryId,
                        principalTable: "DsmEntries",
                        principalColumn: "DsmEntryId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestingEntries",
                columns: table => new
                {
                    TestingId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DsmEntryId = table.Column<int>(type: "INTEGER", nullable: false),
                    FuelType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Amount = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestingEntries", x => x.TestingId);
                    table.ForeignKey(
                        name: "FK_TestingEntries_DsmEntries_DsmEntryId",
                        column: x => x.DsmEntryId,
                        principalTable: "DsmEntries",
                        principalColumn: "DsmEntryId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashDenominations_DsmEntryId_CashType",
                table: "CashDenominations",
                columns: new[] { "DsmEntryId", "CashType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DebitEntries_DsmEntryId",
                table: "DebitEntries",
                column: "DsmEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_DsmEntries_ShiftId_PumpId_DsmName",
                table: "DsmEntries",
                columns: new[] { "ShiftId", "PumpId", "DsmName" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_DsmEntryId",
                table: "Expenses",
                column: "DsmEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ShiftId",
                table: "Expenses",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_NozzleReadings_DsmEntryId",
                table: "NozzleReadings",
                column: "DsmEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCollections_DsmEntryId",
                table: "PaymentCollections",
                column: "DsmEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_ShiftDate_ShiftType",
                table: "Shifts",
                columns: new[] { "ShiftDate", "ShiftType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestingEntries_DsmEntryId",
                table: "TestingEntries",
                column: "DsmEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashDenominations");

            migrationBuilder.DropTable(
                name: "DebitEntries");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "NozzleReadings");

            migrationBuilder.DropTable(
                name: "PaymentCollections");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "TestingEntries");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "DsmEntries");

            migrationBuilder.DropTable(
                name: "Shifts");
        }
    }
}
