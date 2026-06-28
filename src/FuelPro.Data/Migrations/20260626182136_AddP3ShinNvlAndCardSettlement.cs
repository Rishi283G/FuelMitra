using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelPro.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddP3ShinNvlAndCardSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
                        if (!ColumnExists("Shifts", "CardSettlementPosTotal"))
            {
    migrationBuilder.AddColumn<double>(
                    name: "CardSettlementPosTotal",
                    table: "Shifts",
                    type: "REAL",
                    nullable: false,
                    defaultValue: 0.0);
            }

                        if (!ColumnExists("Settings", "CngRate"))
            {
    migrationBuilder.AddColumn<double>(
                    name: "CngRate",
                    table: "Settings",
                    type: "REAL",
                    nullable: false,
                    defaultValue: 85.0);
            }

                        if (!ColumnExists("PaymentCollections", "CardBatch"))
            {
    migrationBuilder.AddColumn<string>(
                    name: "CardBatch",
                    table: "PaymentCollections",
                    type: "TEXT",
                    nullable: true);
            }

                        if (!ColumnExists("PaymentCollections", "CardTid"))
            {
    migrationBuilder.AddColumn<string>(
                    name: "CardTid",
                    table: "PaymentCollections",
                    type: "TEXT",
                    nullable: true);
            }

                        if (!ColumnExists("PaymentCollections", "PetroCardBatch"))
            {
    migrationBuilder.AddColumn<string>(
                    name: "PetroCardBatch",
                    table: "PaymentCollections",
                    type: "TEXT",
                    nullable: true);
            }

                        if (!ColumnExists("PaymentCollections", "PetroCardTid"))
            {
    migrationBuilder.AddColumn<string>(
                    name: "PetroCardTid",
                    table: "PaymentCollections",
                    type: "TEXT",
                    nullable: true);
            }

                        if (!ColumnExists("PaymentCollections", "PhonePeBatch"))
            {
    migrationBuilder.AddColumn<string>(
                    name: "PhonePeBatch",
                    table: "PaymentCollections",
                    type: "TEXT",
                    nullable: true);
            }

                        if (!ColumnExists("PaymentCollections", "PhonePeTid"))
            {
    migrationBuilder.AddColumn<string>(
                    name: "PhonePeTid",
                    table: "PaymentCollections",
                    type: "TEXT",
                    nullable: true);
            }

                        if (!ColumnExists("DebitEntries", "CreatedAt"))
            {
    migrationBuilder.AddColumn<DateTime>(
                    name: "CreatedAt",
                    table: "DebitEntries",
                    type: "TEXT",
                    nullable: false,
                    defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
            }

                        if (!ColumnExists("DebitEntries", "Remarks"))
            {
    migrationBuilder.AddColumn<string>(
                    name: "Remarks",
                    table: "DebitEntries",
                    type: "TEXT",
                    maxLength: 500,
                    nullable: true);
            }

                        if (!ColumnExists("DebitEntries", "SlipNumber"))
            {
    migrationBuilder.AddColumn<string>(
                    name: "SlipNumber",
                    table: "DebitEntries",
                    type: "TEXT",
                    maxLength: 100,
                    nullable: true);
            }

                        if (!ColumnExists("DebitEntries", "VehicleNumber"))
            {
    migrationBuilder.AddColumn<string>(
                    name: "VehicleNumber",
                    table: "DebitEntries",
                    type: "TEXT",
                    maxLength: 50,
                    nullable: true);
            }

                        if (!TableExists("AuditLogs"))
            {
    migrationBuilder.CreateTable(
                    name: "AuditLogs",
                    columns: table => new
                    {
                        AuditLogId = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        TableName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                        RecordId = table.Column<int>(type: "INTEGER", nullable: false),
                        Action = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                        FieldName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                        OldValue = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                        NewValue = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                        ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                        ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                        Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_AuditLogs", x => x.AuditLogId);
                    });
            }

                        if (!TableExists("DayLocks"))
            {
    migrationBuilder.CreateTable(
                    name: "DayLocks",
                    columns: table => new
                    {
                        DayLockId = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        LockDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                        LockedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                        LockedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                        IsLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                        UnlockedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                        UnlockedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                        UnlockReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_DayLocks", x => x.DayLockId);
                    });
            }

                        if (!TableExists("DebtorVehicles"))
            {
    migrationBuilder.CreateTable(
                    name: "DebtorVehicles",
                    columns: table => new
                    {
                        DebtorVehicleId = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        CreditorId = table.Column<int>(type: "INTEGER", nullable: false),
                        VehicleNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                        IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                        CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_DebtorVehicles", x => x.DebtorVehicleId);
                        table.ForeignKey(
                            name: "FK_DebtorVehicles_Creditors_CreditorId",
                            column: x => x.CreditorId,
                            principalTable: "Creditors",
                            principalColumn: "CreditorId",
                            onDelete: ReferentialAction.Cascade);
                    });
            }

                        if (!TableExists("ExpenseCategories"))
            {
    migrationBuilder.CreateTable(
                    name: "ExpenseCategories",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                        IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                        CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_ExpenseCategories", x => x.Id);
                    });
            }

                        if (!TableExists("PumpExpenses"))
            {
    migrationBuilder.CreateTable(
                    name: "PumpExpenses",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        ExpenseDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                        Rent = table.Column<double>(type: "REAL", nullable: false),
                        Salary = table.Column<double>(type: "REAL", nullable: false),
                        TripSheetLoss = table.Column<double>(type: "REAL", nullable: false),
                        DsmShort = table.Column<double>(type: "REAL", nullable: false),
                        BankingExpenses = table.Column<double>(type: "REAL", nullable: false),
                        BpclPortalExpenses = table.Column<double>(type: "REAL", nullable: false),
                        FuelAndTravel = table.Column<double>(type: "REAL", nullable: false),
                        OilPurchase = table.Column<double>(type: "REAL", nullable: false),
                        RepairsAndMaintenance = table.Column<double>(type: "REAL", nullable: false),
                        ElectricityExpenses = table.Column<double>(type: "REAL", nullable: false),
                        OfficeExpenses = table.Column<double>(type: "REAL", nullable: false),
                        PrintingExpense = table.Column<double>(type: "REAL", nullable: false),
                        OtherDescription = table.Column<string>(type: "TEXT", nullable: false),
                        OtherAmount = table.Column<double>(type: "REAL", nullable: false),
                        Remarks = table.Column<string>(type: "TEXT", nullable: false),
                        CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_PumpExpenses", x => x.Id);
                    });
            }

                        if (!TableExists("PumpMappings"))
            {
    migrationBuilder.CreateTable(
                    name: "PumpMappings",
                    columns: table => new
                    {
                        PumpMappingId = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        PumpId = table.Column<int>(type: "INTEGER", nullable: false),
                        NozzleNumber = table.Column<int>(type: "INTEGER", nullable: false),
                        FuelType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                        TankName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                        IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                        CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_PumpMappings", x => x.PumpMappingId);
                    });
            }

                        if (!TableExists("SoftwareVersionHistories"))
            {
    migrationBuilder.CreateTable(
                    name: "SoftwareVersionHistories",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        Version = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                        BuildConfiguration = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                        MachineName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                        DeployedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                        Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_SoftwareVersionHistories", x => x.Id);
                    });
            }

                        if (!TableExists("PumpExpenseCategoryItems"))
            {
    migrationBuilder.CreateTable(
                    name: "PumpExpenseCategoryItems",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        PumpExpenseId = table.Column<int>(type: "INTEGER", nullable: false),
                        CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                        Amount = table.Column<double>(type: "REAL", nullable: false),
                        Remarks = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                        CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_PumpExpenseCategoryItems", x => x.Id);
                        table.ForeignKey(
                            name: "FK_PumpExpenseCategoryItems_ExpenseCategories_CategoryId",
                            column: x => x.CategoryId,
                            principalTable: "ExpenseCategories",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Restrict);
                        table.ForeignKey(
                            name: "FK_PumpExpenseCategoryItems_PumpExpenses_PumpExpenseId",
                            column: x => x.PumpExpenseId,
                            principalTable: "PumpExpenses",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });
            }

                        if (!IndexExists("IX_DayLocks_LockDate"))
            {
    migrationBuilder.CreateIndex(
                    name: "IX_DayLocks_LockDate",
                    table: "DayLocks",
                    column: "LockDate",
                    unique: true);
            }

                        if (!IndexExists("IX_DebtorVehicles_CreditorId_VehicleNumber"))
            {
    migrationBuilder.CreateIndex(
                    name: "IX_DebtorVehicles_CreditorId_VehicleNumber",
                    table: "DebtorVehicles",
                    columns: new[] { "CreditorId", "VehicleNumber" },
                    unique: true);
            }

                        if (!IndexExists("IX_PumpExpenseCategoryItems_CategoryId"))
            {
    migrationBuilder.CreateIndex(
                    name: "IX_PumpExpenseCategoryItems_CategoryId",
                    table: "PumpExpenseCategoryItems",
                    column: "CategoryId");
            }

                        if (!IndexExists("IX_PumpExpenseCategoryItems_PumpExpenseId"))
            {
    migrationBuilder.CreateIndex(
                    name: "IX_PumpExpenseCategoryItems_PumpExpenseId",
                    table: "PumpExpenseCategoryItems",
                    column: "PumpExpenseId");
            }

                        if (!IndexExists("IX_PumpExpenses_ExpenseDate"))
            {
    migrationBuilder.CreateIndex(
                    name: "IX_PumpExpenses_ExpenseDate",
                    table: "PumpExpenses",
                    column: "ExpenseDate",
                    unique: true);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "DayLocks");

            migrationBuilder.DropTable(
                name: "DebtorVehicles");

            migrationBuilder.DropTable(
                name: "PumpExpenseCategoryItems");

            migrationBuilder.DropTable(
                name: "PumpMappings");

            migrationBuilder.DropTable(
                name: "SoftwareVersionHistories");

            migrationBuilder.DropTable(
                name: "ExpenseCategories");

            migrationBuilder.DropTable(
                name: "PumpExpenses");

            migrationBuilder.DropColumn(
                name: "CardSettlementPosTotal",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "CngRate",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "CardBatch",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "CardTid",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PetroCardBatch",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PetroCardTid",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PhonePeBatch",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PhonePeTid",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "DebitEntries");

            migrationBuilder.DropColumn(
                name: "Remarks",
                table: "DebitEntries");

            migrationBuilder.DropColumn(
                name: "SlipNumber",
                table: "DebitEntries");

            migrationBuilder.DropColumn(
                name: "VehicleNumber",
                table: "DebitEntries");
        }
    
        private bool TableExists(string tableName)
        {
            try
            {
                var connString = FuelPro.Data.FuelProDbContext.ConnectionString;
                if (string.IsNullOrEmpty(connString)) return false;
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@name";
                cmd.Parameters.AddWithValue("@name", tableName);
                return cmd.ExecuteScalar() != null;
            }
            catch
            {
                // ignore
            }
            return false;
        }

        private bool ColumnExists(string tableName, string columnName)
        {
            try
            {
                var connString = FuelPro.Data.FuelProDbContext.ConnectionString;
                if (string.IsNullOrEmpty(connString)) return false;
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info({tableName});";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var name = reader["name"]?.ToString();
                    if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
                // ignore
            }
            return false;
        }

        private bool IndexExists(string indexName)
        {
            try
            {
                var connString = FuelPro.Data.FuelProDbContext.ConnectionString;
                if (string.IsNullOrEmpty(connString)) return false;
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name=@name";
                cmd.Parameters.AddWithValue("@name", indexName);
                return cmd.ExecuteScalar() != null;
            }
            catch
            {
                // ignore
            }
            return false;
        }
}
}
