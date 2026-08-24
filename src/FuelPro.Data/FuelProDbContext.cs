using System.Threading;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Models;
using FuelPro.Core.Models.AGS;

namespace FuelPro.Data;

public class FuelProDbContext : DbContext
{
    public static string? ConnectionString { get; set; }

    public FuelProDbContext(DbContextOptions<FuelProDbContext> options) : base(options)
    {
        foreach (var extension in options.Extensions)
        {
            var prop = extension.GetType().GetProperty("ConnectionString");
            if (prop != null)
            {
                var val = prop.GetValue(extension) as string;
                if (!string.IsNullOrEmpty(val))
                {
                    ConnectionString = val;
                    break;
                }
            }
        }
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<DsmEntry> DsmEntries => Set<DsmEntry>();
    public DbSet<NozzleReading> NozzleReadings => Set<NozzleReading>();
    public DbSet<PaymentCollection> PaymentCollections => Set<PaymentCollection>();
    public DbSet<DebitEntry> DebitEntries => Set<DebitEntry>();
    public DbSet<TestingEntry> TestingEntries => Set<TestingEntry>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<CashDenomination> CashDenominations => Set<CashDenomination>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<AppMeta> AppMeta => Set<AppMeta>();
    public DbSet<ShiftOtherCash> ShiftOtherCash => Set<ShiftOtherCash>();
    public DbSet<ShiftFuelRate> ShiftFuelRates => Set<ShiftFuelRate>();
    public DbSet<DsmProfile> DsmProfiles => Set<DsmProfile>();
    public DbSet<CreditorRepayment> CreditorRepayments => Set<CreditorRepayment>();
    public DbSet<Creditor> Creditors => Set<Creditor>();
    public DbSet<SyncChangeLog> SyncChangeLogs => Set<SyncChangeLog>();
    public DbSet<SyncIdMapping> SyncIdMappings => Set<SyncIdMapping>();
    public DbSet<FuelProfitMargin> FuelProfitMargins => Set<FuelProfitMargin>();
    public DbSet<ProductMaster> ProductMasters => Set<ProductMaster>();
    public DbSet<OilDefInventory> OilDefInventories => Set<OilDefInventory>();
    public DbSet<OilDefPurchase> OilDefPurchases => Set<OilDefPurchase>();
    public DbSet<DsmSalaryAdjustment> DsmSalaryAdjustments => Set<DsmSalaryAdjustment>();
    public DbSet<OilDefDailyLog> OilDefDailyLogs => Set<OilDefDailyLog>();
    public DbSet<OuterExpense> OuterExpenses => Set<OuterExpense>();
    public DbSet<DsmUser> DsmUsers => Set<DsmUser>();
    public DbSet<DsmPumpAssignment> DsmPumpAssignments => Set<DsmPumpAssignment>();
    public DbSet<DsmDevice> DsmDevices => Set<DsmDevice>();
    public DbSet<DsmApprovalAudit> DsmApprovalAudits => Set<DsmApprovalAudit>();
    public DbSet<DsmAttendance> DsmAttendance => Set<DsmAttendance>();
    public DbSet<DebtorVehicle> DebtorVehicles => Set<DebtorVehicle>();
    public DbSet<PumpExpense> PumpExpenses => Set<PumpExpense>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DayLock> DayLocks => Set<DayLock>();
    public DbSet<SoftwareVersionHistory> SoftwareVersionHistories => Set<SoftwareVersionHistory>();
    public DbSet<PumpMapping> PumpMappings => Set<PumpMapping>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<PumpExpenseCategoryItem> PumpExpenseCategoryItems => Set<PumpExpenseCategoryItem>();
    public DbSet<DsmPersonalDebtor> DsmPersonalDebtors => Set<DsmPersonalDebtor>();
    public DbSet<DsmPersonalDebtorRepayment> DsmPersonalDebtorRepayments => Set<DsmPersonalDebtorRepayment>();
    public DbSet<PettyCashTransaction> PettyCashTransactions => Set<PettyCashTransaction>();
    public DbSet<KhandharePetroleumEntry> KhandharePetroleumEntries => Set<KhandharePetroleumEntry>();
    public DbSet<DsmQrPaymentEntry> DsmQrPayments => Set<DsmQrPaymentEntry>();

    // Tanker Management
    public DbSet<FuelTanker> FuelTankers => Set<FuelTanker>();
    public DbSet<TankDailyStock> TankDailyStocks => Set<TankDailyStock>();

    // DSM Salary & Payroll
    public DbSet<DsmSalaryHistory> DsmSalaryHistories => Set<DsmSalaryHistory>();
    public DbSet<DsmSalaryPayment> DsmSalaryPayments => Set<DsmSalaryPayment>();


    // AGS Import
    public DbSet<AgsShiftImport> AgsShiftImports => Set<AgsShiftImport>();
    public DbSet<AgsNozzleReading> AgsNozzleReadings => Set<AgsNozzleReading>();
    public DbSet<AgsTankStock> AgsTankStocks => Set<AgsTankStock>();
    public DbSet<AgsDailySummary> AgsDailySummaries => Set<AgsDailySummary>();

    // Dynamic Configuration Masters
    public DbSet<AppFeatureSetting> AppFeatureSettings => Set<AppFeatureSetting>();
    public DbSet<CollectionTypeMaster> CollectionTypes => Set<CollectionTypeMaster>();
    public DbSet<PaymentCollectionItem> PaymentCollectionItems => Set<PaymentCollectionItem>();
    public DbSet<TankDefinition> TankDefinitions => Set<TankDefinition>();
    public DbSet<StationLayoutPreset> StationLayoutPresets => Set<StationLayoutPreset>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)

    {
        base.OnModelCreating(modelBuilder);

        // User
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(e => e.Username).IsUnique();
            entity.Property(e => e.Role).HasDefaultValue("Operator");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        // Shift
        modelBuilder.Entity<Shift>(entity =>
        {
            entity.HasIndex(e => new { e.ShiftDate, e.ShiftType });
            entity.Property(e => e.IsLocked).HasDefaultValue(false);
            entity.Property(e => e.CardSettlementPosTotal).HasDefaultValue(0.0);
        });

        // DsmEntry
        modelBuilder.Entity<DsmEntry>(entity =>
        {
            entity.HasOne(e => e.Shift)
                  .WithMany(s => s.DsmEntries)
                  .HasForeignKey(e => e.ShiftId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.ShiftId, e.PumpId, e.DsmName });
            entity.HasIndex(e => new { e.ShiftId, e.DsmName, e.ReconciledToPumpId });
        });

        // NozzleReading
        modelBuilder.Entity<NozzleReading>(entity =>
        {
            entity.HasOne(e => e.DsmEntry)
                  .WithMany(d => d.NozzleReadings)
                  .HasForeignKey(e => e.DsmEntryId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // PaymentCollection (one-to-one with DsmEntry)
        modelBuilder.Entity<PaymentCollection>(entity =>
        {
            entity.HasOne(e => e.DsmEntry)
                  .WithOne(d => d.PaymentCollection)
                  .HasForeignKey<PaymentCollection>(e => e.DsmEntryId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.PhonePeCardMorning).HasDefaultValue(0.0);
            entity.Property(e => e.PhonePeCardNight).HasDefaultValue(0.0);
            entity.Property(e => e.PhonePeMorning).HasDefaultValue(0.0);
            entity.Property(e => e.PhonePeNight).HasDefaultValue(0.0);
            entity.Property(e => e.CreditCardMorning).HasDefaultValue(0.0);
            entity.Property(e => e.CreditCardNight).HasDefaultValue(0.0);
            entity.Property(e => e.PetroCardMorning).HasDefaultValue(0.0);
            entity.Property(e => e.PetroCardNight).HasDefaultValue(0.0);

            entity.Property(e => e.CardTid).IsRequired(false);
            entity.Property(e => e.CardBatch).IsRequired(false);
            entity.Property(e => e.PhonePeTid).IsRequired(false);
            entity.Property(e => e.PhonePeBatch).IsRequired(false);
            entity.Property(e => e.PetroCardTid).IsRequired(false);
            entity.Property(e => e.PetroCardBatch).IsRequired(false);
            entity.Property(e => e.PhonePeTidMorning).IsRequired(false);
            entity.Property(e => e.PhonePeBatchMorning).IsRequired(false);
            entity.Property(e => e.PhonePeTidNight).IsRequired(false);
            entity.Property(e => e.PhonePeBatchNight).IsRequired(false);

            entity.Property(e => e.CreditCardTidMorning).IsRequired(false);
            entity.Property(e => e.CreditCardBatchMorning).IsRequired(false);
            entity.Property(e => e.CreditCardTidNight).IsRequired(false);
            entity.Property(e => e.CreditCardBatchNight).IsRequired(false);
            entity.Property(e => e.PetroCardTidMorning).IsRequired(false);
            entity.Property(e => e.PetroCardBatchMorning).IsRequired(false);
            entity.Property(e => e.PetroCardTidNight).IsRequired(false);
            entity.Property(e => e.PetroCardBatchNight).IsRequired(false);

            // Computed properties are ignored in EF
            entity.Ignore(e => e.PhonePe);
            entity.Ignore(e => e.PhonePeCard);
            entity.Ignore(e => e.CreditCard);
            entity.Ignore(e => e.PetroCard);
        });

        // Creditor
        modelBuilder.Entity<Creditor>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasMany(e => e.Vehicles)
                  .WithOne(v => v.Creditor)
                  .HasForeignKey(v => v.CreditorId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // DebtorVehicle
        modelBuilder.Entity<DebtorVehicle>(entity =>
        {
            entity.HasIndex(e => new { e.CreditorId, e.VehicleNumber }).IsUnique();
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        // DebitEntry
        modelBuilder.Entity<DebitEntry>(entity =>
        {
            entity.HasOne(e => e.DsmEntry)
                  .WithMany(d => d.DebitEntries)
                  .HasForeignKey(e => e.DsmEntryId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // TestingEntry
        modelBuilder.Entity<TestingEntry>(entity =>
        {
            entity.HasOne(e => e.DsmEntry)
                  .WithMany(d => d.TestingEntries)
                  .HasForeignKey(e => e.DsmEntryId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Expense
        modelBuilder.Entity<Expense>(entity =>
        {
            entity.HasOne(e => e.DsmEntry)
                  .WithMany(d => d.Expenses)
                  .HasForeignKey(e => e.DsmEntryId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Shift)
                  .WithMany(s => s.Expenses)
                  .HasForeignKey(e => e.ShiftId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // CashDenomination
        modelBuilder.Entity<CashDenomination>(entity =>
        {
            entity.HasOne(e => e.DsmEntry)
                  .WithMany(d => d.CashDenominations)
                  .HasForeignKey(e => e.DsmEntryId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.DsmEntryId, e.CashType }).IsUnique();
        });

        // Settings
        modelBuilder.Entity<Setting>(entity =>
        {
            entity.Property(e => e.HsdRate).HasDefaultValue(90.35);
            entity.Property(e => e.MsIRate).HasDefaultValue(103.81);
            entity.Property(e => e.MsIIRate).HasDefaultValue(103.81);
            entity.Property(e => e.CngRate).HasDefaultValue(85.0);
            entity.Property(e => e.PumpStationName).HasDefaultValue("Mitali Service Station");
        });

        // PumpExpense
        modelBuilder.Entity<PumpExpense>(entity =>
        {
            entity.HasIndex(e => e.ExpenseDate).IsUnique();
        });

        // PumpExpenseCategoryItem
        modelBuilder.Entity<PumpExpenseCategoryItem>(entity =>
        {
            entity.HasOne(e => e.PumpExpense)
                  .WithMany()
                  .HasForeignKey(e => e.PumpExpenseId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Category)
                  .WithMany()
                  .HasForeignKey(e => e.CategoryId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // DayLock
        modelBuilder.Entity<DayLock>(entity =>
        {
            entity.HasIndex(e => e.LockDate).IsUnique();
        });

        modelBuilder.Entity<AppMeta>(entity =>
        {
            entity.HasKey(e => e.Key);
        });

        // ShiftOtherCash
        modelBuilder.Entity<ShiftOtherCash>(entity =>
        {
            entity.HasOne(e => e.Shift)
                  .WithMany()
                  .HasForeignKey(e => e.ShiftId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => new { e.ShiftDate, e.ShiftNumber });
            entity.Property(e => e.IsEditable).HasDefaultValue(true);
        });

        // ShiftFuelRate
        modelBuilder.Entity<ShiftFuelRate>(entity =>
        {
            entity.HasOne(e => e.Shift)
                  .WithMany()
                  .HasForeignKey(e => e.ShiftId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => new { e.ShiftDate, e.ShiftNumber, e.FuelType }).IsUnique();
        });

        // DsmProfile
        modelBuilder.Entity<DsmProfile>(entity =>
        {
            entity.HasIndex(e => e.DsmName).IsUnique();
        });

        // ─── AGS Import ───────────────────────────────────────────────
        modelBuilder.Entity<AgsShiftImport>(entity =>
        {
            entity.HasIndex(e => new { e.ImportDate, e.ShiftType, e.IsActive });
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<AgsNozzleReading>(entity =>
        {
            entity.HasOne(e => e.AgsShiftImport)
                  .WithMany(s => s.NozzleReadings)
                  .HasForeignKey(e => e.AgsShiftImportId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgsTankStock>(entity =>
        {
            entity.HasOne(e => e.AgsShiftImport)
                  .WithMany(s => s.TankStocks)
                  .HasForeignKey(e => e.AgsShiftImportId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgsDailySummary>(entity =>
        {
            entity.HasIndex(e => e.SummaryDate).IsUnique();
            entity.Property(e => e.NozzleDaySalesJson).HasDefaultValue("{}");
            entity.Property(e => e.ShiftBreakdownJson).HasDefaultValue("{}");
        });

        // SyncIdMapping
        modelBuilder.Entity<SyncIdMapping>(entity =>
        {
            entity.HasIndex(e => new { e.TableName, e.RemoteGuid }).IsUnique();
        });

        // DsmUser
        modelBuilder.Entity<DsmUser>(entity =>
        {
            entity.HasIndex(e => e.MobileNumber).IsUnique();
            entity.HasIndex(e => e.EmployeeCode);
        });

        // DsmPumpAssignment
        modelBuilder.Entity<DsmPumpAssignment>(entity =>
        {
            entity.HasOne(e => e.DsmUser)
                  .WithMany()
                  .HasForeignKey(e => e.DsmUserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.PumpId, e.ShiftType, e.IsActive });
        });

        // DsmDevice
        modelBuilder.Entity<DsmDevice>(entity =>
        {
            entity.HasOne(e => e.DsmUser)
                  .WithMany()
                  .HasForeignKey(e => e.DsmUserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.DeviceId);
        });

        // DsmApprovalAudit
        modelBuilder.Entity<DsmApprovalAudit>(entity =>
        {
            entity.HasIndex(e => e.SubmissionId);
        });

        // DsmAttendance
        modelBuilder.Entity<DsmAttendance>(entity =>
        {
            entity.HasOne(e => e.DsmUser)
                  .WithMany()
                  .HasForeignKey(e => e.DsmUserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.DsmUserId, e.AttendanceDate, e.ShiftType }).IsUnique();
        });

        // DsmPersonalDebtor
        modelBuilder.Entity<DsmPersonalDebtor>(entity =>
        {
            entity.HasOne(e => e.DsmEntry)
                  .WithMany(d => d.PersonalDebtors)
                  .HasForeignKey(e => e.DsmEntryId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.SyncGuid).IsUnique();
            entity.HasIndex(e => new { e.DsmName, e.Date });
        });

        // DsmPersonalDebtorRepayment
        modelBuilder.Entity<DsmPersonalDebtorRepayment>(entity =>
        {
            entity.HasOne(e => e.DsmPersonalDebtor)
                  .WithMany()
                  .HasForeignKey(e => e.DsmPersonalDebtorId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Shift)
                  .WithMany()
                  .HasForeignKey(e => e.ShiftId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.SyncGuid).IsUnique();
        });

        // KhandharePetroleumEntry
        modelBuilder.Entity<KhandharePetroleumEntry>(entity =>
        {
            entity.HasOne(e => e.DsmEntry)
                  .WithMany(d => d.KhandharePetroleumEntries)
                  .HasForeignKey(e => e.DsmEntryId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.SyncGuid).IsUnique();
            entity.HasIndex(e => new { e.DsmName, e.Date });
        });

        // DsmQrPaymentEntry
        modelBuilder.Entity<DsmQrPaymentEntry>(entity =>
        {
            entity.HasOne(e => e.DsmEntry)
                  .WithMany(d => d.QrPayments)
                  .HasForeignKey(e => e.DsmEntryId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.SyncGuid).IsUnique();
            entity.HasIndex(e => new { e.DsmName, e.Date });
            entity.HasIndex(e => e.TargetDsmName);
        });
    }

    public override int SaveChanges()
    {
        var changes = CaptureChanges();
        try
        {
            var result = base.SaveChanges();
            SaveChangeLogs(changes);
            return result;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            foreach (var entry in ex.Entries)
            {
                var keyValues = entry.Metadata.FindPrimaryKey()?.Properties
                    .Select(p => $"{p.Name}: {entry.Property(p.Name).CurrentValue}")
                    .ToList();
                Serilog.Log.Error("Concurrency Exception on Entity: {EntityType}, State: {State}, Keys: {Keys}",
                    entry.Entity.GetType().FullName,
                    entry.State,
                    string.Join(", ", keyValues ?? new List<string>()));
            }
            throw;
        }
    }

    public override async Task<int> SaveChangesAsync(System.Threading.CancellationToken cancellationToken = default)
    {
        var changes = CaptureChanges();
        try
        {
            var result = await base.SaveChangesAsync(cancellationToken);
            await SaveChangeLogsAsync(changes);
            return result;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            foreach (var entry in ex.Entries)
            {
                var keyValues = entry.Metadata.FindPrimaryKey()?.Properties
                    .Select(p => $"{p.Name}: {entry.Property(p.Name).CurrentValue}")
                    .ToList();
                Serilog.Log.Error("Concurrency Exception on Entity: {EntityType}, State: {State}, Keys: {Keys}",
                    entry.Entity.GetType().FullName,
                    entry.State,
                    string.Join(", ", keyValues ?? new List<string>()));
            }
            throw;
        }
    }

    private class CapturedChange
    {
        public object Entity { get; set; } = null!;
        public string TableName { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public int RecordId { get; set; }
        public string? RecordGuid { get; set; }
    }

    private static readonly AsyncLocal<bool> _bypassTracking = new();
    public static bool BypassTracking
    {
        get => _bypassTracking.Value;
        set => _bypassTracking.Value = value;
    }

    private List<CapturedChange> CaptureChanges()
    {
        var list = new List<CapturedChange>();
        if (BypassTracking) return list;

        var entries = ChangeTracker.Entries();

        foreach (var entry in entries)
        {
            if (entry.Entity is SyncChangeLog || entry.Entity is OuterExpense) continue;
            if (entry.Entity.GetType().Namespace?.StartsWith("FuelPro.Core.Models") != true) continue;

            if (entry.State == EntityState.Added)
            {
                list.Add(new CapturedChange
                {
                    Entity = entry.Entity,
                    TableName = entry.Metadata.GetTableName() ?? entry.Entity.GetType().Name,
                    Operation = "INSERT"
                });
            }
            else if (entry.State == EntityState.Modified)
            {
                list.Add(new CapturedChange
                {
                    Entity = entry.Entity,
                    TableName = entry.Metadata.GetTableName() ?? entry.Entity.GetType().Name,
                    Operation = "UPDATE",
                    RecordId = GetPrimaryKeyValue(entry.Entity)
                });
            }
            else if (entry.State == EntityState.Deleted)
            {
                var tableName = entry.Metadata.GetTableName() ?? entry.Entity.GetType().Name;
                var localId = GetPrimaryKeyValue(entry.Entity);
                
                string? remoteGuid = null;
                try
                {
                    var mapping = SyncIdMappings.Local.FirstOrDefault(m => m.TableName == tableName && m.LocalId == localId);
                    remoteGuid = mapping?.RemoteGuid;
                    if (remoteGuid == null)
                    {
                        remoteGuid = SyncIdMappings.AsNoTracking()
                            .Where(m => m.TableName == tableName && m.LocalId == localId)
                            .Select(m => m.RemoteGuid)
                            .FirstOrDefault();
                    }
                }
                catch
                {
                    // Ignore errors during delete query
                }

                list.Add(new CapturedChange
                {
                    Entity = entry.Entity,
                    TableName = tableName,
                    Operation = "DELETE",
                    RecordId = localId,
                    RecordGuid = remoteGuid
                });
            }
        }

        return list;
    }

    private string? _cachedStationId;
    private string? _cachedMachineId;

    private void PopulateStationAndMachine(SyncChangeLog log)
    {
        try
        {
            if (string.IsNullOrEmpty(_cachedStationId))
            {
                _cachedStationId = AppMeta.AsNoTracking().FirstOrDefault(m => m.Key == "Sync.StationId")?.Value ?? "";
            }
            if (string.IsNullOrEmpty(_cachedMachineId))
            {
                _cachedMachineId = AppMeta.AsNoTracking().FirstOrDefault(m => m.Key == "Sync.MachineId")?.Value ?? "";
            }
            log.StationId = string.IsNullOrEmpty(_cachedStationId) ? null : _cachedStationId;
            log.MachineId = string.IsNullOrEmpty(_cachedMachineId) ? null : _cachedMachineId;
        }
        catch
        {
            // Fallback in case AppMeta is not accessible or not created yet
        }
    }

    private void SaveChangeLogs(List<CapturedChange> changes)
    {
        if (changes.Count == 0) return;

        foreach (var change in changes)
        {
            var recordId = change.Operation == "INSERT" ? GetPrimaryKeyValue(change.Entity) : change.RecordId;
            if (recordId == 0) continue; // Skip if no valid key

            var log = new SyncChangeLog
            {
                TableName = change.TableName,
                RecordId = recordId,
                Operation = change.Operation,
                CreatedAt = DateTime.Now,
                IsSynced = false,
                SyncGuid = Guid.NewGuid().ToString(),
                RecordGuid = change.RecordGuid
            };
            PopulateStationAndMachine(log);
            SyncChangeLogs.Add(log);
        }

        base.SaveChanges();
    }

    private async Task SaveChangeLogsAsync(List<CapturedChange> changes)
    {
        if (changes.Count == 0) return;

        foreach (var change in changes)
        {
            var recordId = change.Operation == "INSERT" ? GetPrimaryKeyValue(change.Entity) : change.RecordId;
            if (recordId == 0) continue;

            var log = new SyncChangeLog
            {
                TableName = change.TableName,
                RecordId = recordId,
                Operation = change.Operation,
                CreatedAt = DateTime.Now,
                IsSynced = false,
                SyncGuid = Guid.NewGuid().ToString(),
                RecordGuid = change.RecordGuid
            };
            PopulateStationAndMachine(log);
            SyncChangeLogs.Add(log);
        }

        await base.SaveChangesAsync();
    }

    private int GetPrimaryKeyValue(object entity)
    {
        var entry = Entry(entity);
        var keyProperty = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (keyProperty == null) return 0;
        var value = entry.Property(keyProperty.Name).CurrentValue;
        if (value == null) return 0;
        if (int.TryParse(value.ToString(), out var intVal))
        {
            return intVal;
        }
        return 0;
    }
}
