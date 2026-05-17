using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Models;
using FuelPro.Core.Models.AGS;

namespace FuelPro.Data;

public class FuelProDbContext : DbContext
{
    public FuelProDbContext(DbContextOptions<FuelProDbContext> options) : base(options) { }

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

    // AGS Import
    public DbSet<AgsShiftImport> AgsShiftImports => Set<AgsShiftImport>();
    public DbSet<AgsNozzleReading> AgsNozzleReadings => Set<AgsNozzleReading>();
    public DbSet<AgsTankStock> AgsTankStocks => Set<AgsTankStock>();
    public DbSet<AgsDailySummary> AgsDailySummaries => Set<AgsDailySummary>();

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
            entity.HasIndex(e => new { e.ShiftDate, e.ShiftType }).IsUnique();
            entity.Property(e => e.IsLocked).HasDefaultValue(false);
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
            entity.Property(e => e.PetroCard).HasDefaultValue(0.0);

            // Computed properties are ignored in EF
            entity.Ignore(e => e.PhonePe);
            entity.Ignore(e => e.PhonePeCard);
            entity.Ignore(e => e.CreditCard);
        });

        // Creditor
        modelBuilder.Entity<Creditor>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique();
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
            entity.Property(e => e.PumpStationName).HasDefaultValue("VKD Petroleum");
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
    }
}
