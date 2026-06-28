using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelPro.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDsmPhase1Tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!ColumnExists("DsmProfiles", "BaseSalary"))
            {
                migrationBuilder.AddColumn<double>(
                    name: "BaseSalary",
                    table: "DsmProfiles",
                    type: "REAL",
                    nullable: false,
                    defaultValue: 0.0);
            }

            if (!ColumnExists("DsmProfiles", "JoiningDate"))
            {
                migrationBuilder.AddColumn<DateTime>(
                    name: "JoiningDate",
                    table: "DsmProfiles",
                    type: "TEXT",
                    nullable: true);
            }

            if (!ColumnExists("DsmProfiles", "SalaryType"))
            {
                migrationBuilder.AddColumn<string>(
                    name: "SalaryType",
                    table: "DsmProfiles",
                    type: "TEXT",
                    maxLength: 50,
                    nullable: false,
                    defaultValue: "");
            }

                        if (!TableExists("DsmApprovalAudits"))
            {
    migrationBuilder.CreateTable(
                    name: "DsmApprovalAudits",
                    columns: table => new
                    {
                        DsmApprovalAuditId = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        SubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                        OriginalDataJson = table.Column<string>(type: "TEXT", nullable: false),
                        ApprovedDataJson = table.Column<string>(type: "TEXT", nullable: false),
                        ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                        ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                        Remarks = table.Column<string>(type: "TEXT", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_DsmApprovalAudits", x => x.DsmApprovalAuditId);
                    });
            }

                        if (!TableExists("DsmSalaryAdjustments"))
            {
    migrationBuilder.CreateTable(
                    name: "DsmSalaryAdjustments",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        DsmName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                        Year = table.Column<int>(type: "INTEGER", nullable: false),
                        Month = table.Column<int>(type: "INTEGER", nullable: false),
                        AdvancePaid = table.Column<double>(type: "REAL", nullable: false),
                        OtherAdjustments = table.Column<double>(type: "REAL", nullable: false),
                        Remarks = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_DsmSalaryAdjustments", x => x.Id);
                    });
            }

                        if (!TableExists("DsmUsers"))
            {
    migrationBuilder.CreateTable(
                    name: "DsmUsers",
                    columns: table => new
                    {
                        DsmUserId = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        EmployeeCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                        FullName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                        MobileNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                        AuthUserId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                        IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                        CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_DsmUsers", x => x.DsmUserId);
                    });
            }

                        if (!TableExists("FuelProfitMargins"))
            {
    migrationBuilder.CreateTable(
                    name: "FuelProfitMargins",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        EffectiveDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                        FuelType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                        MarginPerLitre = table.Column<double>(type: "REAL", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_FuelProfitMargins", x => x.Id);
                    });
            }

                        if (!TableExists("OuterExpenses"))
            {
    migrationBuilder.CreateTable(
                    name: "OuterExpenses",
                    columns: table => new
                    {
                        OuterExpenseId = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                        Amount = table.Column<double>(type: "REAL", nullable: false),
                        ExpenseDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                        CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_OuterExpenses", x => x.OuterExpenseId);
                    });
            }

                        if (!TableExists("ProductMasters"))
            {
    migrationBuilder.CreateTable(
                    name: "ProductMasters",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        ProductName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                        Category = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                        Unit = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                        DefaultSaleRate = table.Column<double>(type: "REAL", nullable: false),
                        IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_ProductMasters", x => x.Id);
                    });
            }

                        if (!TableExists("SyncChangeLogs"))
            {
    migrationBuilder.CreateTable(
                    name: "SyncChangeLogs",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        TableName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                        RecordId = table.Column<int>(type: "INTEGER", nullable: false),
                        Operation = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                        CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                        IsSynced = table.Column<bool>(type: "INTEGER", nullable: false),
                        SyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                        StationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                        MachineId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                        SyncGuid = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                        RecordGuid = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_SyncChangeLogs", x => x.Id);
                    });
            }

                        if (!TableExists("SyncIdMappings"))
            {
    migrationBuilder.CreateTable(
                    name: "SyncIdMappings",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        TableName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                        RemoteGuid = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                        LocalId = table.Column<int>(type: "INTEGER", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_SyncIdMappings", x => x.Id);
                    });
            }

                        if (!TableExists("DsmAttendance"))
            {
    migrationBuilder.CreateTable(
                    name: "DsmAttendance",
                    columns: table => new
                    {
                        DsmAttendanceId = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        DsmUserId = table.Column<int>(type: "INTEGER", nullable: false),
                        AttendanceDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                        ShiftType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                        ClockInTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                        ClockOutTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                        Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_DsmAttendance", x => x.DsmAttendanceId);
                        table.ForeignKey(
                            name: "FK_DsmAttendance_DsmUsers_DsmUserId",
                            column: x => x.DsmUserId,
                            principalTable: "DsmUsers",
                            principalColumn: "DsmUserId",
                            onDelete: ReferentialAction.Cascade);
                    });
            }

                        if (!TableExists("DsmDevices"))
            {
    migrationBuilder.CreateTable(
                    name: "DsmDevices",
                    columns: table => new
                    {
                        DsmDeviceId = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        DsmUserId = table.Column<int>(type: "INTEGER", nullable: false),
                        DeviceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                        DeviceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                        LastLogin = table.Column<DateTime>(type: "TEXT", nullable: false),
                        LastSeen = table.Column<DateTime>(type: "TEXT", nullable: false),
                        IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_DsmDevices", x => x.DsmDeviceId);
                        table.ForeignKey(
                            name: "FK_DsmDevices_DsmUsers_DsmUserId",
                            column: x => x.DsmUserId,
                            principalTable: "DsmUsers",
                            principalColumn: "DsmUserId",
                            onDelete: ReferentialAction.Cascade);
                    });
            }

                        if (!TableExists("DsmPumpAssignments"))
            {
    migrationBuilder.CreateTable(
                    name: "DsmPumpAssignments",
                    columns: table => new
                    {
                        DsmPumpAssignmentId = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        DsmUserId = table.Column<int>(type: "INTEGER", nullable: false),
                        PumpId = table.Column<int>(type: "INTEGER", nullable: false),
                        ShiftType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                        IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                        AssignedDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_DsmPumpAssignments", x => x.DsmPumpAssignmentId);
                        table.ForeignKey(
                            name: "FK_DsmPumpAssignments_DsmUsers_DsmUserId",
                            column: x => x.DsmUserId,
                            principalTable: "DsmUsers",
                            principalColumn: "DsmUserId",
                            onDelete: ReferentialAction.Cascade);
                    });
            }

                        if (!TableExists("OilDefDailyLogs"))
            {
    migrationBuilder.CreateTable(
                    name: "OilDefDailyLogs",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        LogDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                        ProductType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                        ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                        OverrideSaleRate = table.Column<double>(type: "REAL", nullable: true),
                        AddedQuantity = table.Column<double>(type: "REAL", nullable: false),
                        SoldQuantity = table.Column<double>(type: "REAL", nullable: false),
                        RemainingStock = table.Column<double>(type: "REAL", nullable: false),
                        AdjustmentQuantity = table.Column<double>(type: "REAL", nullable: false),
                        AdjustmentType = table.Column<string>(type: "TEXT", nullable: true),
                        Remarks = table.Column<string>(type: "TEXT", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_OilDefDailyLogs", x => x.Id);
                        table.ForeignKey(
                            name: "FK_OilDefDailyLogs_ProductMasters_ProductId",
                            column: x => x.ProductId,
                            principalTable: "ProductMasters",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });
            }

                        if (!TableExists("OilDefInventories"))
            {
    migrationBuilder.CreateTable(
                    name: "OilDefInventories",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        Year = table.Column<int>(type: "INTEGER", nullable: false),
                        Month = table.Column<int>(type: "INTEGER", nullable: false),
                        ProductType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                        ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                        OpeningStock = table.Column<double>(type: "REAL", nullable: false),
                        ClosingStock = table.Column<double>(type: "REAL", nullable: false),
                        SalePrice = table.Column<double>(type: "REAL", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_OilDefInventories", x => x.Id);
                        table.ForeignKey(
                            name: "FK_OilDefInventories_ProductMasters_ProductId",
                            column: x => x.ProductId,
                            principalTable: "ProductMasters",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });
            }

                        if (!TableExists("OilDefPurchases"))
            {
    migrationBuilder.CreateTable(
                    name: "OilDefPurchases",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        ProductType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                        ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                        SupplierName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                        InvoiceNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                        PurchaseDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                        Quantity = table.Column<double>(type: "REAL", nullable: false),
                        UnitPrice = table.Column<double>(type: "REAL", nullable: false),
                        TotalCost = table.Column<double>(type: "REAL", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_OilDefPurchases", x => x.Id);
                        table.ForeignKey(
                            name: "FK_OilDefPurchases_ProductMasters_ProductId",
                            column: x => x.ProductId,
                            principalTable: "ProductMasters",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });
            }

                        if (!IndexExists("IX_DsmApprovalAudits_SubmissionId"))
            {
    migrationBuilder.CreateIndex(
                    name: "IX_DsmApprovalAudits_SubmissionId",
                    table: "DsmApprovalAudits",
                    column: "SubmissionId");
            }

                        if (!IndexExists("IX_DsmAttendance_DsmUserId_AttendanceDate_ShiftType"))
            {
    migrationBuilder.CreateIndex(
                    name: "IX_DsmAttendance_DsmUserId_AttendanceDate_ShiftType",
                    table: "DsmAttendance",
                    columns: new[] { "DsmUserId", "AttendanceDate", "ShiftType" },
                    unique: true);
            }

                        if (!IndexExists("IX_DsmDevices_DeviceId"))
            {
    migrationBuilder.CreateIndex(
                    name: "IX_DsmDevices_DeviceId",
                    table: "DsmDevices",
                    column: "DeviceId");
            }

                        if (!IndexExists("IX_DsmDevices_DsmUserId"))
            {
    migrationBuilder.CreateIndex(
                    name: "IX_DsmDevices_DsmUserId",
                    table: "DsmDevices",
                    column: "DsmUserId");
            }

                        if (!IndexExists("IX_DsmPumpAssignments_DsmUserId"))
            {
    migrationBuilder.CreateIndex(
                    name: "IX_DsmPumpAssignments_DsmUserId",
                    table: "DsmPumpAssignments",
                    column: "DsmUserId");
            }

                        if (!IndexExists("IX_DsmPumpAssignments_PumpId_ShiftType_IsActive"))
            {
    migrationBuilder.CreateIndex(
                    name: "IX_DsmPumpAssignments_PumpId_ShiftType_IsActive",
                    table: "DsmPumpAssignments",
                    columns: new[] { "PumpId", "ShiftType", "IsActive" });
            }

                        if (!IndexExists("IX_DsmUsers_EmployeeCode"))
            {
    migrationBuilder.CreateIndex(
                    name: "IX_DsmUsers_EmployeeCode",
                    table: "DsmUsers",
                    column: "EmployeeCode");
            }

                        if (!IndexExists("IX_DsmUsers_MobileNumber"))
            {
    migrationBuilder.CreateIndex(
                    name: "IX_DsmUsers_MobileNumber",
                    table: "DsmUsers",
                    column: "MobileNumber",
                    unique: true);
            }

                        if (!IndexExists("IX_OilDefDailyLogs_ProductId"))
            {
    migrationBuilder.CreateIndex(
                    name: "IX_OilDefDailyLogs_ProductId",
                    table: "OilDefDailyLogs",
                    column: "ProductId");
            }

                        if (!IndexExists("IX_OilDefInventories_ProductId"))
            {
    migrationBuilder.CreateIndex(
                    name: "IX_OilDefInventories_ProductId",
                    table: "OilDefInventories",
                    column: "ProductId");
            }

                        if (!IndexExists("IX_OilDefPurchases_ProductId"))
            {
    migrationBuilder.CreateIndex(
                    name: "IX_OilDefPurchases_ProductId",
                    table: "OilDefPurchases",
                    column: "ProductId");
            }

                        if (!IndexExists("IX_SyncIdMappings_TableName_RemoteGuid"))
            {
    migrationBuilder.CreateIndex(
                    name: "IX_SyncIdMappings_TableName_RemoteGuid",
                    table: "SyncIdMappings",
                    columns: new[] { "TableName", "RemoteGuid" },
                    unique: true);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DsmApprovalAudits");

            migrationBuilder.DropTable(
                name: "DsmAttendance");

            migrationBuilder.DropTable(
                name: "DsmDevices");

            migrationBuilder.DropTable(
                name: "DsmPumpAssignments");

            migrationBuilder.DropTable(
                name: "DsmSalaryAdjustments");

            migrationBuilder.DropTable(
                name: "FuelProfitMargins");

            migrationBuilder.DropTable(
                name: "OilDefDailyLogs");

            migrationBuilder.DropTable(
                name: "OilDefInventories");

            migrationBuilder.DropTable(
                name: "OilDefPurchases");

            migrationBuilder.DropTable(
                name: "OuterExpenses");

            migrationBuilder.DropTable(
                name: "SyncChangeLogs");

            migrationBuilder.DropTable(
                name: "SyncIdMappings");

            migrationBuilder.DropTable(
                name: "DsmUsers");

            migrationBuilder.DropTable(
                name: "ProductMasters");

            migrationBuilder.DropColumn(
                name: "BaseSalary",
                table: "DsmProfiles");

            migrationBuilder.DropColumn(
                name: "JoiningDate",
                table: "DsmProfiles");

            migrationBuilder.DropColumn(
                name: "SalaryType",
                table: "DsmProfiles");
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
