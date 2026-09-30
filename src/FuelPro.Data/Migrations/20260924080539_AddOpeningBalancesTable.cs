using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelPro.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOpeningBalancesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppFeatureSettingsJson",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CollectionTypesJson",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FuelRatesJson",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PumpConnectionRulesJson",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PumpMappingsJson",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Shift1Manager",
                table: "Settings",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Shift2Manager",
                table: "Settings",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Shift3Manager",
                table: "Settings",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TankDefinitionsJson",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DynamicItemsJson",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleNumber",
                table: "KhandharePetroleumEntries",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConnectedPumpIdsJson",
                table: "DsmPumpAssignments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntryType",
                table: "DsmPersonalDebtors",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "Operational");

            migrationBuilder.AddColumn<string>(
                name: "ConnectedPumpIdsJson",
                table: "DsmEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppFeatureSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FeatureKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TargetRole = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppFeatureSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CollectionTypes",
                columns: table => new
                {
                    CollectionTypeId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    HasTidBatch = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionTypes", x => x.CollectionTypeId);
                });

            migrationBuilder.CreateTable(
                name: "DsmQrPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DsmEntryId = table.Column<int>(type: "INTEGER", nullable: true),
                    DsmName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TargetDsmName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Amount = table.Column<double>(type: "REAL", nullable: false),
                    Tid = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Batch = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Slot = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SyncGuid = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DsmQrPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DsmQrPayments_DsmEntries_DsmEntryId",
                        column: x => x.DsmEntryId,
                        principalTable: "DsmEntries",
                        principalColumn: "DsmEntryId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OpeningBalances",
                columns: table => new
                {
                    OpeningBalanceId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SyncGuid = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EntityIdentifier = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreditorId = table.Column<int>(type: "INTEGER", nullable: true),
                    OpeningDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Amount = table.Column<double>(type: "REAL", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBalances", x => x.OpeningBalanceId);
                    table.ForeignKey(
                        name: "FK_OpeningBalances_Creditors_CreditorId",
                        column: x => x.CreditorId,
                        principalTable: "Creditors",
                        principalColumn: "CreditorId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PaymentCollectionItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PaymentId = table.Column<int>(type: "INTEGER", nullable: false),
                    CollectionTypeCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Amount = table.Column<double>(type: "REAL", nullable: false),
                    Tid = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Batch = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Slot = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentCollectionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentCollectionItems_PaymentCollections_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "PaymentCollections",
                        principalColumn: "PaymentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StationLayoutPresets",
                columns: table => new
                {
                    PresetId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PresetCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PresetName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    LayoutJson = table.Column<string>(type: "TEXT", nullable: false),
                    PumpCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NozzleCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TankCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationLayoutPresets", x => x.PresetId);
                });

            migrationBuilder.CreateTable(
                name: "TankDefinitions",
                columns: table => new
                {
                    TankId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TankName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CapacityKL = table.Column<double>(type: "REAL", nullable: false),
                    FuelType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasTesting = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TankDefinitions", x => x.TankId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DsmQrPayments_DsmEntryId",
                table: "DsmQrPayments",
                column: "DsmEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_DsmQrPayments_DsmName_Date",
                table: "DsmQrPayments",
                columns: new[] { "DsmName", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_DsmQrPayments_SyncGuid",
                table: "DsmQrPayments",
                column: "SyncGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DsmQrPayments_TargetDsmName",
                table: "DsmQrPayments",
                column: "TargetDsmName");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalances_CreditorId",
                table: "OpeningBalances",
                column: "CreditorId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalances_EntityType_EntityIdentifier",
                table: "OpeningBalances",
                columns: new[] { "EntityType", "EntityIdentifier" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalances_SyncGuid",
                table: "OpeningBalances",
                column: "SyncGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCollectionItems_PaymentId",
                table: "PaymentCollectionItems",
                column: "PaymentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppFeatureSettings");

            migrationBuilder.DropTable(
                name: "CollectionTypes");

            migrationBuilder.DropTable(
                name: "DsmQrPayments");

            migrationBuilder.DropTable(
                name: "OpeningBalances");

            migrationBuilder.DropTable(
                name: "PaymentCollectionItems");

            migrationBuilder.DropTable(
                name: "StationLayoutPresets");

            migrationBuilder.DropTable(
                name: "TankDefinitions");

            migrationBuilder.DropColumn(
                name: "AppFeatureSettingsJson",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "CollectionTypesJson",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "FuelRatesJson",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "PumpConnectionRulesJson",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "PumpMappingsJson",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Shift1Manager",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Shift2Manager",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Shift3Manager",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "TankDefinitionsJson",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "DynamicItemsJson",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "VehicleNumber",
                table: "KhandharePetroleumEntries");

            migrationBuilder.DropColumn(
                name: "ConnectedPumpIdsJson",
                table: "DsmPumpAssignments");

            migrationBuilder.DropColumn(
                name: "EntryType",
                table: "DsmPersonalDebtors");

            migrationBuilder.DropColumn(
                name: "ConnectedPumpIdsJson",
                table: "DsmEntries");
        }
    }
}
