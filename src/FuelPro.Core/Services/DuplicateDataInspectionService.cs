using FuelPro.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FuelPro.Core.Services;

/// <summary>
/// Scans the database for duplicate and orphan records without modifying any data.
/// All detection uses EF Core LINQ — no raw SQL.
/// </summary>
public class DuplicateDataInspectionService : IDuplicateDataInspectionService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger = Log.ForContext<DuplicateDataInspectionService>();
    private DataIntegrityReport? _cachedReport;

    public DuplicateDataInspectionService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<DataIntegrityReport> RunFullScanAsync()
    {
        _logger.Information("Starting full data integrity scan...");

        var report = new DataIntegrityReport { ScannedAt = DateTime.Now };

        report.DsmEntryGroups = await FindDuplicateDsmEntriesAsync();
        report.DuplicateDsmEntries = report.DsmEntryGroups.Sum(g => g.DuplicateCount);

        report.DebitEntryGroups = await FindDuplicateDebitEntriesAsync();
        report.DuplicateDebitEntries = report.DebitEntryGroups.Sum(g => g.DuplicateCount);

        report.RepaymentGroups = await FindDuplicateRepaymentsAsync();
        report.DuplicateRepayments = report.RepaymentGroups.Sum(g => g.DuplicateCount);

        report.Orphans = await FindOrphanRecordsAsync();
        report.OrphanRecords = report.Orphans.Count;

        report.BrokenForeignKeys = await CountBrokenForeignKeysAsync();

        _cachedReport = report;

        _logger.Information(
            "Integrity scan complete. DsmDuplicates={D} DebitDuplicates={De} RepaymentDuplicates={R} Orphans={O} BrokenFKs={B}",
            report.DuplicateDsmEntries, report.DuplicateDebitEntries,
            report.DuplicateRepayments, report.OrphanRecords, report.BrokenForeignKeys);

        return report;
    }

    public DataIntegrityReport? GetCachedReport() => _cachedReport;

    // ── Detection methods ─────────────────────────────────────────────────────

    public async Task<List<DsmEntryDuplicateGroup>> FindDuplicateDsmEntriesAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = GetDbContext(scope);

            // Load all entries with navigations needed for child counts
            var entries = await db.Set<DsmEntry>()
                .Include(e => e.Shift)
                .Include(e => e.PaymentCollection)
                .Include(e => e.DebitEntries)
                .Include(e => e.NozzleReadings)
                .Include(e => e.Expenses)
                .Include(e => e.TestingEntries)
                .Include(e => e.CashDenominations)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();

            // Group by business key: ShiftId + PumpId + DsmName (case-insensitive)
            var groups = entries
                .GroupBy(e => new
                {
                    e.ShiftId,
                    e.PumpId,
                    DsmName = (e.DsmName ?? string.Empty).Trim().ToLowerInvariant()
                })
                .Where(g => g.Count() > 1 && (g.Any(e => e.PaymentCollection == null || e.NozzleReadings == null || e.NozzleReadings.Count == 0) || IsIdenticalNozzleReadings(g.ToList())))
                .ToList();

            var result = new List<DsmEntryDuplicateGroup>();
            foreach (var group in groups)
            {
                var ordered = group.OrderBy(e => e.CreatedAt).ToList();
                result.Add(new DsmEntryDuplicateGroup
                {
                    OriginalRecord = ordered.First(),
                    DuplicateRecords = ordered.Skip(1).ToList(),
                    Reason = "Same ShiftId + PumpId + DsmName (case-insensitive)"
                });
            }

            _logger.Information("Found {Count} DsmEntry duplicate groups", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to find duplicate DsmEntries");
            return new List<DsmEntryDuplicateGroup>();
        }
    }

    public async Task<List<DebitEntryDuplicateGroup>> FindDuplicateDebitEntriesAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = GetDbContext(scope);

            var entries = await db.Set<DebitEntry>()
                .Include(d => d.DsmEntry)
                .OrderBy(d => d.DebitId)
                .ToListAsync();

            var groups = entries
                .GroupBy(d => new
                {
                    ShiftId = d.DsmEntry?.ShiftId ?? 0,
                    DebtorName = (d.DebtorName ?? string.Empty).Trim().ToLowerInvariant(),
                    d.Amount
                })
                .Where(g => g.Count() > 1)
                .ToList();

            var result = new List<DebitEntryDuplicateGroup>();
            foreach (var group in groups)
            {
                var ordered = group.OrderBy(d => d.DebitId).ToList();
                result.Add(new DebitEntryDuplicateGroup
                {
                    OriginalRecord = ordered.First(),
                    DuplicateRecords = ordered.Skip(1).ToList(),
                    Reason = "Same ShiftId + DebtorName + Amount"
                });
            }

            _logger.Information("Found {Count} DebitEntry duplicate groups", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to find duplicate DebitEntries");
            return new List<DebitEntryDuplicateGroup>();
        }
    }

    public async Task<List<RepaymentDuplicateGroup>> FindDuplicateRepaymentsAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = GetDbContext(scope);

            var repayments = await db.Set<CreditorRepayment>()
                .OrderBy(r => r.CreditorRepaymentId)
                .ToListAsync();

            var groups = repayments
                .GroupBy(r => new
                {
                    CreditorName = (r.CreditorName ?? string.Empty).Trim().ToLowerInvariant(),
                    RepaymentDate = r.RepaymentDate.Date,
                    r.Amount,
                    PaymentMode = (r.PaymentMode ?? string.Empty).Trim().ToLowerInvariant()
                })
                .Where(g => g.Count() > 1)
                .ToList();

            var result = new List<RepaymentDuplicateGroup>();
            foreach (var group in groups)
            {
                var ordered = group.OrderBy(r => r.CreditorRepaymentId).ToList();
                result.Add(new RepaymentDuplicateGroup
                {
                    OriginalRecord = ordered.First(),
                    DuplicateRecords = ordered.Skip(1).ToList(),
                    Reason = "Same CreditorName + RepaymentDate + Amount + PaymentMode"
                });
            }

            _logger.Information("Found {Count} CreditorRepayment duplicate groups", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to find duplicate CreditorRepayments");
            return new List<RepaymentDuplicateGroup>();
        }
    }

    public async Task<List<OrphanRecord>> FindOrphanRecordsAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = GetDbContext(scope);

            var orphans = new List<OrphanRecord>();

            var validDsmEntryIdsList = await db.Set<DsmEntry>()
                .Select(e => e.DsmEntryId)
                .ToListAsync();
            var validDsmEntryIds = new HashSet<int>(validDsmEntryIdsList);

            // Orphan NozzleReadings
            var orphanNozzles = await db.Set<NozzleReading>()
                .Where(n => !validDsmEntryIds.Contains(n.DsmEntryId))
                .Select(n => n.NozzleReadingId)
                .ToListAsync();
            orphans.AddRange(orphanNozzles.Select(id => new OrphanRecord
            {
                EntityType = "NozzleReading",
                EntityId = id,
                Description = $"NozzleReading {id} has no parent DsmEntry"
            }));

            // Orphan DebitEntries
            var orphanDebits = await db.Set<DebitEntry>()
                .Where(d => !validDsmEntryIds.Contains(d.DsmEntryId))
                .Select(d => d.DebitId)
                .ToListAsync();
            orphans.AddRange(orphanDebits.Select(id => new OrphanRecord
            {
                EntityType = "DebitEntry",
                EntityId = id,
                Description = $"DebitEntry {id} has no parent DsmEntry"
            }));

            // Orphan Expenses
            var orphanExpenses = await db.Set<Expense>()
                .Where(e => e.DsmEntryId.HasValue && !validDsmEntryIds.Contains(e.DsmEntryId!.Value))
                .Select(e => e.ExpenseId)
                .ToListAsync();
            orphans.AddRange(orphanExpenses.Select(id => new OrphanRecord
            {
                EntityType = "Expense",
                EntityId = id,
                Description = $"Expense {id} references a non-existent DsmEntry"
            }));

            // Orphan CashDenominations
            var orphanCash = await db.Set<CashDenomination>()
                .Where(c => !validDsmEntryIds.Contains(c.DsmEntryId))
                .Select(c => c.CashDenomId)
                .ToListAsync();
            orphans.AddRange(orphanCash.Select(id => new OrphanRecord
            {
                EntityType = "CashDenomination",
                EntityId = id,
                Description = $"CashDenomination {id} has no parent DsmEntry"
            }));

            // Orphan PaymentCollections
            var orphanPayments = await db.Set<PaymentCollection>()
                .Where(p => !validDsmEntryIds.Contains(p.DsmEntryId))
                .Select(p => p.PaymentId)
                .ToListAsync();
            orphans.AddRange(orphanPayments.Select(id => new OrphanRecord
            {
                EntityType = "PaymentCollection",
                EntityId = id,
                Description = $"PaymentCollection {id} has no parent DsmEntry"
            }));

            _logger.Information("Found {Count} orphan records", orphans.Count);
            return orphans;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to find orphan records");
            return new List<OrphanRecord>();
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<int> CountBrokenForeignKeysAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = GetDbContext(scope);

            var validShiftIdsList = await db.Set<Shift>()
                .Select(s => s.ShiftId)
                .ToListAsync();
            var validShiftIds = new HashSet<int>(validShiftIdsList);

            // DsmEntries with invalid ShiftId
            var brokenDsm = await db.Set<DsmEntry>()
                .CountAsync(e => !validShiftIds.Contains(e.ShiftId));

            return brokenDsm;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to count broken foreign keys");
            return 0;
        }
    }

    private static bool IsIdenticalNozzleReadings(List<DsmEntry> entries)
    {
        if (entries == null || entries.Count < 2) return false;
        for (int i = 0; i < entries.Count; i++)
        {
            for (int j = i + 1; j < entries.Count; j++)
            {
                var r1 = entries[i].NozzleReadings.OrderBy(n => n.NozzleNumber).ToList();
                var r2 = entries[j].NozzleReadings.OrderBy(n => n.NozzleNumber).ToList();
                if (r1.Count > 0 && r1.Count == r2.Count)
                {
                    bool match = true;
                    for (int k = 0; k < r1.Count; k++)
                    {
                        if (Math.Abs(r1[k].OpeningReading - r2[k].OpeningReading) > 0.01 ||
                            Math.Abs(r1[k].ClosingReading - r2[k].ClosingReading) > 0.01)
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) return true;
                }
            }
        }
        return false;
    }

    private static DbContext GetDbContext(IServiceScope scope)
    {
        // Resolve via base DbContext (mapped in DI)
        return scope.ServiceProvider.GetRequiredService<DbContext>();
    }
}
