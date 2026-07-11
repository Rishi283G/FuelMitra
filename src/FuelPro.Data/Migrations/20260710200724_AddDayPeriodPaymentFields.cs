using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelPro.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDayPeriodPaymentFields : Migration
    {
        private string GetConnectionString()
        {
            var connString = FuelPro.Data.FuelProDbContext.ConnectionString;
            if (!string.IsNullOrEmpty(connString)) return connString;

            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FuelPro", "fuelPro.db");
            return $"Data Source={dbPath}";
        }

        private bool ColumnExists(string tableName, string columnName)
        {
            try
            {
                var connString = GetConnectionString();
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

        private bool TableExists(string tableName)
        {
            try
            {
                var connString = GetConnectionString();
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

        private bool IndexExists(string indexName)
        {
            try
            {
                var connString = GetConnectionString();
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

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            void SafeAddColumn<T>(
                string table,
                string name,
                string type = null,
                bool nullable = true,
                object defaultValue = null,
                int? maxLength = null)
            {
                if (!ColumnExists(table, name))
                {
                    migrationBuilder.AddColumn<T>(
                        name: name,
                        table: table,
                        type: type,
                        nullable: nullable,
                        defaultValue: defaultValue,
                        maxLength: maxLength);
                }
            }
                        if (ColumnExists("PaymentCollections", "PetroCard") && !ColumnExists("PaymentCollections", "PetroCardNight"))
            {
                migrationBuilder.RenameColumn(
                    name: "PetroCard",
                    table: "PaymentCollections",
                    newName: "PetroCardNight");
            }
            else if (ColumnExists("PaymentCollections", "PetroCard"))
            {
                migrationBuilder.DropColumn(
                    name: "PetroCard",
                    table: "PaymentCollections");
            }

            migrationBuilder.AlterColumn<string>(
                name: "PumpStationName",
                table: "Settings",
                type: "TEXT",
                maxLength: 300,
                nullable: false,
                defaultValue: "Mitali Service Station",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 300,
                oldDefaultValue: "PyroSync");

            SafeAddColumn<string>(
                name: "CreditCardBatchDay",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<string>(
                name: "CreditCardBatchMorning",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<string>(
                name: "CreditCardBatchNight",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<double>(
                name: "CreditCardDay",
                table: "PaymentCollections",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            SafeAddColumn<string>(
                name: "CreditCardTidDay",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<string>(
                name: "CreditCardTidMorning",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<string>(
                name: "CreditCardTidNight",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<string>(
                name: "PetroCardBatchDay",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<string>(
                name: "PetroCardBatchMorning",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<string>(
                name: "PetroCardBatchNight",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<double>(
                name: "PetroCardDay",
                table: "PaymentCollections",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            SafeAddColumn<double>(
                name: "PetroCardMorning",
                table: "PaymentCollections",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            SafeAddColumn<string>(
                name: "PetroCardTidDay",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<string>(
                name: "PetroCardTidMorning",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<string>(
                name: "PetroCardTidNight",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<string>(
                name: "PhonePeBatchDay",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<string>(
                name: "PhonePeBatchMorning",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<string>(
                name: "PhonePeBatchNight",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<double>(
                name: "PhonePeCardDay",
                table: "PaymentCollections",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            SafeAddColumn<double>(
                name: "PhonePeDay",
                table: "PaymentCollections",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            SafeAddColumn<string>(
                name: "PhonePeTidDay",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<string>(
                name: "PhonePeTidMorning",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<string>(
                name: "PhonePeTidNight",
                table: "PaymentCollections",
                type: "TEXT",
                nullable: true);

            SafeAddColumn<double>(
                name: "PendingAdvanceDeduction",
                table: "DsmSalaryAdjustments",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            SafeAddColumn<int>(
                name: "ConnectedPumpId",
                table: "DsmPumpAssignments",
                type: "INTEGER",
                nullable: true);

            SafeAddColumn<string>(
                name: "MobileNumber",
                table: "DsmProfiles",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            SafeAddColumn<double>(
                name: "MonthlyAdvanceDeduction",
                table: "DsmProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            SafeAddColumn<double>(
                name: "PendingAdvance",
                table: "DsmProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            SafeAddColumn<string>(
                name: "EndTime",
                table: "DsmEntries",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            SafeAddColumn<string>(
                name: "StartTime",
                table: "DsmEntries",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            SafeAddColumn<string>(
                name: "CardBatch",
                table: "DebitEntries",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            SafeAddColumn<string>(
                name: "CardTid",
                table: "DebitEntries",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            SafeAddColumn<int>(
                name: "Coins",
                table: "DebitEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            SafeAddColumn<int>(
                name: "Denom10",
                table: "DebitEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            SafeAddColumn<int>(
                name: "Denom100",
                table: "DebitEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            SafeAddColumn<int>(
                name: "Denom20",
                table: "DebitEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            SafeAddColumn<int>(
                name: "Denom200",
                table: "DebitEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            SafeAddColumn<int>(
                name: "Denom50",
                table: "DebitEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            SafeAddColumn<int>(
                name: "Denom500",
                table: "DebitEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            SafeAddColumn<string>(
                name: "EntryTime",
                table: "DebitEntries",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            SafeAddColumn<string>(
                name: "Fuel",
                table: "DebitEntries",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            SafeAddColumn<string>(
                name: "PaymentMethod",
                table: "DebitEntries",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            SafeAddColumn<string>(
                name: "CardBatch",
                table: "CreditorRepayments",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            SafeAddColumn<string>(
                name: "CardTid",
                table: "CreditorRepayments",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            SafeAddColumn<int>(
                name: "Coins",
                table: "CreditorRepayments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            SafeAddColumn<int>(
                name: "Denom10",
                table: "CreditorRepayments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            SafeAddColumn<int>(
                name: "Denom100",
                table: "CreditorRepayments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            SafeAddColumn<int>(
                name: "Denom20",
                table: "CreditorRepayments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            SafeAddColumn<int>(
                name: "Denom200",
                table: "CreditorRepayments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            SafeAddColumn<int>(
                name: "Denom50",
                table: "CreditorRepayments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            SafeAddColumn<int>(
                name: "Denom500",
                table: "CreditorRepayments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            SafeAddColumn<string>(
                name: "ShiftNumber",
                table: "CreditorRepayments",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            if (!TableExists("DsmPersonalDebtors"))
            {
                migrationBuilder.CreateTable(
                    name: "DsmPersonalDebtors",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        SyncGuid = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                        DsmEntryId = table.Column<int>(type: "INTEGER", nullable: true),
                        DsmName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                        Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                        Time = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                        Amount = table.Column<double>(type: "REAL", nullable: false),
                        FuelProduct = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                        Remarks = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                        PaymentMethod = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                        Denom500 = table.Column<int>(type: "INTEGER", nullable: false),
                        Denom200 = table.Column<int>(type: "INTEGER", nullable: false),
                        Denom100 = table.Column<int>(type: "INTEGER", nullable: false),
                        Denom50 = table.Column<int>(type: "INTEGER", nullable: false),
                        Denom20 = table.Column<int>(type: "INTEGER", nullable: false),
                        Denom10 = table.Column<int>(type: "INTEGER", nullable: false),
                        Coins = table.Column<int>(type: "INTEGER", nullable: false),
                        CardTid = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                        CardBatch = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                        SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                        RepaidAmount = table.Column<double>(type: "REAL", nullable: false),
                        DeductFromSalary = table.Column<bool>(type: "INTEGER", nullable: false),
                        CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_DsmPersonalDebtors", x => x.Id);
                        table.ForeignKey(
                            name: "FK_DsmPersonalDebtors_DsmEntries_DsmEntryId",
                            column: x => x.DsmEntryId,
                            principalTable: "DsmEntries",
                            principalColumn: "DsmEntryId",
                            onDelete: ReferentialAction.SetNull);
                    });
            }

            if (!TableExists("PettyCashTransactions"))
            {
                migrationBuilder.CreateTable(
                    name: "PettyCashTransactions",
                    columns: table => new
                    {
                        TransactionId = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                        Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                        Amount = table.Column<double>(type: "REAL", nullable: false),
                        Type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                        ShiftExpenseId = table.Column<int>(type: "INTEGER", nullable: true),
                        CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_PettyCashTransactions", x => x.TransactionId);
                    });
            }

            if (!TableExists("DsmPersonalDebtorRepayments"))
            {
                migrationBuilder.CreateTable(
                    name: "DsmPersonalDebtorRepayments",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        SyncGuid = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                        DsmPersonalDebtorId = table.Column<int>(type: "INTEGER", nullable: false),
                        ShiftId = table.Column<int>(type: "INTEGER", nullable: true),
                        Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                        Amount = table.Column<double>(type: "REAL", nullable: false),
                        PaymentMethod = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                        Denom500 = table.Column<int>(type: "INTEGER", nullable: false),
                        Denom200 = table.Column<int>(type: "INTEGER", nullable: false),
                        Denom100 = table.Column<int>(type: "INTEGER", nullable: false),
                        Denom50 = table.Column<int>(type: "INTEGER", nullable: false),
                        Denom20 = table.Column<int>(type: "INTEGER", nullable: false),
                        Denom10 = table.Column<int>(type: "INTEGER", nullable: false),
                        Coins = table.Column<int>(type: "INTEGER", nullable: false),
                        CardTid = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                        CardBatch = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                        Source = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                        CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_DsmPersonalDebtorRepayments", x => x.Id);
                        table.ForeignKey(
                            name: "FK_DsmPersonalDebtorRepayments_DsmPersonalDebtors_DsmPersonalDebtorId",
                            column: x => x.DsmPersonalDebtorId,
                            principalTable: "DsmPersonalDebtors",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                        table.ForeignKey(
                            name: "FK_DsmPersonalDebtorRepayments_Shifts_ShiftId",
                            column: x => x.ShiftId,
                            principalTable: "Shifts",
                            principalColumn: "ShiftId",
                            onDelete: ReferentialAction.SetNull);
                    });
            }

            if (!IndexExists("IX_DsmPersonalDebtorRepayments_DsmPersonalDebtorId"))
            {
                migrationBuilder.CreateIndex(
                    name: "IX_DsmPersonalDebtorRepayments_DsmPersonalDebtorId",
                    table: "DsmPersonalDebtorRepayments",
                    column: "DsmPersonalDebtorId");
            }

            if (!IndexExists("IX_DsmPersonalDebtorRepayments_ShiftId"))
            {
                migrationBuilder.CreateIndex(
                    name: "IX_DsmPersonalDebtorRepayments_ShiftId",
                    table: "DsmPersonalDebtorRepayments",
                    column: "ShiftId");
            }

            if (!IndexExists("IX_DsmPersonalDebtorRepayments_SyncGuid"))
            {
                migrationBuilder.CreateIndex(
                    name: "IX_DsmPersonalDebtorRepayments_SyncGuid",
                    table: "DsmPersonalDebtorRepayments",
                    column: "SyncGuid",
                    unique: true);
            }

            if (!IndexExists("IX_DsmPersonalDebtors_DsmEntryId"))
            {
                migrationBuilder.CreateIndex(
                    name: "IX_DsmPersonalDebtors_DsmEntryId",
                    table: "DsmPersonalDebtors",
                    column: "DsmEntryId");
            }

            if (!IndexExists("IX_DsmPersonalDebtors_DsmName_Date"))
            {
                migrationBuilder.CreateIndex(
                    name: "IX_DsmPersonalDebtors_DsmName_Date",
                    table: "DsmPersonalDebtors",
                    columns: new[] { "DsmName", "Date" });
            }

            if (!IndexExists("IX_DsmPersonalDebtors_SyncGuid"))
            {
                migrationBuilder.CreateIndex(
                    name: "IX_DsmPersonalDebtors_SyncGuid",
                    table: "DsmPersonalDebtors",
                    column: "SyncGuid",
                    unique: true);
            }

            SafeAddColumn<int>(
                name: "ConnectedPumpId",
                table: "DsmEntries",
                type: "INTEGER",
                nullable: true);

            SafeAddColumn<int>(
                name: "ReconciledToPumpId",
                table: "DsmEntries",
                type: "INTEGER",
                nullable: true);

            SafeAddColumn<bool>(
                name: "IsReconciled",
                table: "DsmEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            SafeAddColumn<double>(
                name: "CashDeposit",
                table: "PaymentCollections",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            SafeAddColumn<double>(
                name: "PhonePeMorning",
                table: "PaymentCollections",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            SafeAddColumn<double>(
                name: "PhonePeNight",
                table: "PaymentCollections",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            SafeAddColumn<double>(
                name: "PhonePeCardMorning",
                table: "PaymentCollections",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            SafeAddColumn<double>(
                name: "PhonePeCardNight",
                table: "PaymentCollections",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DsmPersonalDebtorRepayments");

            migrationBuilder.DropTable(
                name: "PettyCashTransactions");

            migrationBuilder.DropTable(
                name: "DsmPersonalDebtors");

            migrationBuilder.DropColumn(
                name: "CreditCardBatchDay",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "CreditCardBatchMorning",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "CreditCardBatchNight",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "CreditCardDay",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "CreditCardTidDay",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "CreditCardTidMorning",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "CreditCardTidNight",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PetroCardBatchDay",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PetroCardBatchMorning",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PetroCardBatchNight",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PetroCardDay",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PetroCardMorning",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PetroCardTidDay",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PetroCardTidMorning",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PetroCardTidNight",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PhonePeBatchDay",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PhonePeBatchMorning",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PhonePeBatchNight",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PhonePeCardDay",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PhonePeDay",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PhonePeTidDay",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PhonePeTidMorning",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PhonePeTidNight",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "PendingAdvanceDeduction",
                table: "DsmSalaryAdjustments");

            migrationBuilder.DropColumn(
                name: "ConnectedPumpId",
                table: "DsmPumpAssignments");

            migrationBuilder.DropColumn(
                name: "MobileNumber",
                table: "DsmProfiles");

            migrationBuilder.DropColumn(
                name: "MonthlyAdvanceDeduction",
                table: "DsmProfiles");

            migrationBuilder.DropColumn(
                name: "PendingAdvance",
                table: "DsmProfiles");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "DsmEntries");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "DsmEntries");

            migrationBuilder.DropColumn(
                name: "CardBatch",
                table: "DebitEntries");

            migrationBuilder.DropColumn(
                name: "CardTid",
                table: "DebitEntries");

            migrationBuilder.DropColumn(
                name: "Coins",
                table: "DebitEntries");

            migrationBuilder.DropColumn(
                name: "Denom10",
                table: "DebitEntries");

            migrationBuilder.DropColumn(
                name: "Denom100",
                table: "DebitEntries");

            migrationBuilder.DropColumn(
                name: "Denom20",
                table: "DebitEntries");

            migrationBuilder.DropColumn(
                name: "Denom200",
                table: "DebitEntries");

            migrationBuilder.DropColumn(
                name: "Denom50",
                table: "DebitEntries");

            migrationBuilder.DropColumn(
                name: "Denom500",
                table: "DebitEntries");

            migrationBuilder.DropColumn(
                name: "EntryTime",
                table: "DebitEntries");

            migrationBuilder.DropColumn(
                name: "Fuel",
                table: "DebitEntries");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "DebitEntries");

            migrationBuilder.DropColumn(
                name: "CardBatch",
                table: "CreditorRepayments");

            migrationBuilder.DropColumn(
                name: "CardTid",
                table: "CreditorRepayments");

            migrationBuilder.DropColumn(
                name: "Coins",
                table: "CreditorRepayments");

            migrationBuilder.DropColumn(
                name: "Denom10",
                table: "CreditorRepayments");

            migrationBuilder.DropColumn(
                name: "Denom100",
                table: "CreditorRepayments");

            migrationBuilder.DropColumn(
                name: "Denom20",
                table: "CreditorRepayments");

            migrationBuilder.DropColumn(
                name: "Denom200",
                table: "CreditorRepayments");

            migrationBuilder.DropColumn(
                name: "Denom50",
                table: "CreditorRepayments");

            migrationBuilder.DropColumn(
                name: "Denom500",
                table: "CreditorRepayments");

            migrationBuilder.DropColumn(
                name: "ShiftNumber",
                table: "CreditorRepayments");

            migrationBuilder.RenameColumn(
                name: "PetroCardNight",
                table: "PaymentCollections",
                newName: "PetroCard");

            migrationBuilder.AlterColumn<string>(
                name: "PumpStationName",
                table: "Settings",
                type: "TEXT",
                maxLength: 300,
                nullable: false,
                defaultValue: "PyroSync",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 300,
                oldDefaultValue: "Mitali Service Station");
        }
    }
}
