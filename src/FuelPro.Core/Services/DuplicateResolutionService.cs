using FuelPro.Core.Common;
using FuelPro.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FuelPro.Core.Services;

/// <summary>
/// Safely removes confirmed duplicate records from the database.
/// The surviving (original, earliest CreatedAt) record is never modified.
/// All operations run inside a transaction; any failure rolls back completely.
/// </summary>
public class DuplicateResolutionService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger = Log.ForContext<DuplicateResolutionService>();

    public DuplicateResolutionService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    // ── DsmEntry resolution ──────────────────────────────────────────────────

    /// <summary>
    /// Permanently deletes a duplicate DsmEntry and all its child rows.
    /// The surviving entry is never touched.
    /// </summary>
    public async Task<Result<string>> DeleteDuplicateDsmEntryAsync(int duplicateEntryId, int survivingEntryId)
    {
        if (duplicateEntryId == survivingEntryId)
            return Result<string>.Fail("Duplicate and surviving entry IDs are the same. Nothing deleted.");

        if (duplicateEntryId <= 0 || survivingEntryId <= 0)
            return Result<string>.Fail("Invalid entry IDs provided.");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();

            // Validate both entries exist
            var duplicate = await db.Set<DsmEntry>()
                .Include(e => e.NozzleReadings)
                .Include(e => e.PaymentCollection)
                .Include(e => e.DebitEntries)
                .Include(e => e.TestingEntries)
                .Include(e => e.Expenses)
                .Include(e => e.CashDenominations)
                .Include(e => e.PersonalDebtors)
                .FirstOrDefaultAsync(e => e.DsmEntryId == duplicateEntryId);

            if (duplicate == null)
                return Result<string>.Fail($"DsmEntry {duplicateEntryId} not found.");

            var surviving = await db.Set<DsmEntry>()
                .FirstOrDefaultAsync(e => e.DsmEntryId == survivingEntryId);

            if (surviving == null)
                return Result<string>.Fail($"Surviving DsmEntry {survivingEntryId} not found.");

            // Safety: surviving must be older (or equal age — allow same timestamp edge case)
            if (surviving.CreatedAt > duplicate.CreatedAt.AddSeconds(1))
            {
                return Result<string>.Fail(
                    $"Safety violation: surviving entry {survivingEntryId} (created {surviving.CreatedAt:g}) " +
                    $"is newer than duplicate entry {duplicateEntryId} (created {duplicate.CreatedAt:g}). " +
                    "Swap the arguments or verify which is the original.");
            }

            await using var transaction = await db.Database.BeginTransactionAsync();

            try
            {
                int childCount = 0;

                // Delete child records of the duplicate
                if (duplicate.NozzleReadings?.Count > 0)
                {
                    db.Set<NozzleReading>().RemoveRange(duplicate.NozzleReadings);
                    childCount += duplicate.NozzleReadings.Count;
                }

                if (duplicate.DebitEntries?.Count > 0)
                {
                    db.Set<DebitEntry>().RemoveRange(duplicate.DebitEntries);
                    childCount += duplicate.DebitEntries.Count;
                }

                if (duplicate.TestingEntries?.Count > 0)
                {
                    db.Set<TestingEntry>().RemoveRange(duplicate.TestingEntries);
                    childCount += duplicate.TestingEntries.Count;
                }

                if (duplicate.Expenses?.Count > 0)
                {
                    db.Set<Expense>().RemoveRange(duplicate.Expenses);
                    childCount += duplicate.Expenses.Count;
                }

                if (duplicate.CashDenominations?.Count > 0)
                {
                    db.Set<CashDenomination>().RemoveRange(duplicate.CashDenominations);
                    childCount += duplicate.CashDenominations.Count;
                }

                if (duplicate.PersonalDebtors?.Count > 0)
                {
                    db.Set<DsmPersonalDebtor>().RemoveRange(duplicate.PersonalDebtors);
                    childCount += duplicate.PersonalDebtors.Count;
                }

                if (duplicate.PaymentCollection != null)
                {
                    db.Set<PaymentCollection>().Remove(duplicate.PaymentCollection);
                    childCount++;
                }

                // Delete the duplicate parent
                db.Set<DsmEntry>().Remove(duplicate);

                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                var msg = $"Successfully deleted duplicate DsmEntry {duplicateEntryId} " +
                          $"(DSM: {duplicate.DsmName}, Pump: {duplicate.PumpId}) " +
                          $"and {childCount} child rows. Surviving entry: {survivingEntryId}.";

                _logger.Information(msg);
                DsmEntryService.RaiseDsmEntryChanged();
                return Result<string>.Ok(msg);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete duplicate DsmEntry {DuplicateId}", duplicateEntryId);
            return Result<string>.Fail($"Delete failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Permanently deletes multiple duplicate DsmEntries and all their child rows in a single transaction.
    /// The surviving entries are never touched.
    /// </summary>
    public async Task<Result<string>> DeleteMultipleDuplicateDsmEntriesAsync(IEnumerable<(int DuplicateEntryId, int SurvivingEntryId)> pairs)
    {
        var pairList = pairs?.ToList() ?? new List<(int, int)>();
        if (pairList.Count == 0)
            return Result<string>.Fail("No duplicate DsmEntry pairs provided.");

        var duplicateIds = pairList.Select(p => p.DuplicateEntryId).Distinct().ToList();

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();

            var duplicates = await db.Set<DsmEntry>()
                .Include(e => e.NozzleReadings)
                .Include(e => e.PaymentCollection)
                .Include(e => e.DebitEntries)
                .Include(e => e.TestingEntries)
                .Include(e => e.Expenses)
                .Include(e => e.CashDenominations)
                .Include(e => e.PersonalDebtors)
                .Where(e => duplicateIds.Contains(e.DsmEntryId))
                .ToListAsync();

            if (duplicates.Count == 0)
                return Result<string>.Fail("None of the specified duplicate DsmEntries were found.");

            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                int childCount = 0;
                foreach (var duplicate in duplicates)
                {
                    if (duplicate.NozzleReadings?.Count > 0)
                    {
                        db.Set<NozzleReading>().RemoveRange(duplicate.NozzleReadings);
                        childCount += duplicate.NozzleReadings.Count;
                    }
                    if (duplicate.DebitEntries?.Count > 0)
                    {
                        db.Set<DebitEntry>().RemoveRange(duplicate.DebitEntries);
                        childCount += duplicate.DebitEntries.Count;
                    }
                    if (duplicate.TestingEntries?.Count > 0)
                    {
                        db.Set<TestingEntry>().RemoveRange(duplicate.TestingEntries);
                        childCount += duplicate.TestingEntries.Count;
                    }
                    if (duplicate.Expenses?.Count > 0)
                    {
                        db.Set<Expense>().RemoveRange(duplicate.Expenses);
                        childCount += duplicate.Expenses.Count;
                    }
                    if (duplicate.CashDenominations?.Count > 0)
                    {
                        db.Set<CashDenomination>().RemoveRange(duplicate.CashDenominations);
                        childCount += duplicate.CashDenominations.Count;
                    }
                    if (duplicate.PersonalDebtors?.Count > 0)
                    {
                        db.Set<DsmPersonalDebtor>().RemoveRange(duplicate.PersonalDebtors);
                        childCount += duplicate.PersonalDebtors.Count;
                    }
                    if (duplicate.PaymentCollection != null)
                    {
                        db.Set<PaymentCollection>().Remove(duplicate.PaymentCollection);
                        childCount++;
                    }
                    db.Set<DsmEntry>().Remove(duplicate);
                }

                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                var msg = $"Successfully deleted {duplicates.Count} duplicate DsmEntries and {childCount} child rows.";
                _logger.Information(msg);
                DsmEntryService.RaiseDsmEntryChanged();
                return Result<string>.Ok(msg);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to batch delete duplicate DsmEntries");
            return Result<string>.Fail($"Batch delete failed: {ex.Message}");
        }
    }


    // ── DebitEntry resolution ────────────────────────────────────────────────

    /// <summary>
    /// Permanently deletes the specified duplicate DebitEntry rows.
    /// The original (lowest ID) record is preserved.
    /// </summary>
    public async Task<Result<string>> DeleteDuplicateDebitEntriesAsync(List<int> duplicateDebitEntryIds)
    {
        if (duplicateDebitEntryIds == null || duplicateDebitEntryIds.Count == 0)
            return Result<string>.Fail("No DebitEntry IDs provided.");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();

            var toDelete = await db.Set<DebitEntry>()
                .Where(d => duplicateDebitEntryIds.Contains(d.DebitId))
                .ToListAsync();

            if (toDelete.Count == 0)
                return Result<string>.Fail("None of the specified DebitEntry IDs were found.");

            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                db.Set<DebitEntry>().RemoveRange(toDelete);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                var msg = $"Deleted {toDelete.Count} duplicate DebitEntry row(s): IDs [{string.Join(", ", toDelete.Select(d => d.DebitId))}].";
                _logger.Information(msg);
                return Result<string>.Ok(msg);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete duplicate DebitEntries");
            return Result<string>.Fail($"Delete failed: {ex.Message}");
        }
    }

    // ── CreditorRepayment resolution ─────────────────────────────────────────

    /// <summary>
    /// Permanently deletes the specified duplicate CreditorRepayment rows.
    /// </summary>
    public async Task<Result<string>> DeleteDuplicateRepaymentsAsync(List<int> duplicateRepaymentIds)
    {
        if (duplicateRepaymentIds == null || duplicateRepaymentIds.Count == 0)
            return Result<string>.Fail("No repayment IDs provided.");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();

            var toDelete = await db.Set<CreditorRepayment>()
                .Where(r => duplicateRepaymentIds.Contains(r.CreditorRepaymentId))
                .ToListAsync();

            if (toDelete.Count == 0)
                return Result<string>.Fail("None of the specified repayment IDs were found.");

            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                db.Set<CreditorRepayment>().RemoveRange(toDelete);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                var msg = $"Deleted {toDelete.Count} duplicate CreditorRepayment row(s): IDs [{string.Join(", ", toDelete.Select(r => r.CreditorRepaymentId))}].";
                _logger.Information(msg);
                return Result<string>.Ok(msg);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete duplicate CreditorRepayments");
            return Result<string>.Fail($"Delete failed: {ex.Message}");
        }
    }

    // ── Orphan cleanup ────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes orphan child records (child rows whose parent DsmEntry no longer exists).
    /// Safe to run as these rows are unreachable by any report query.
    /// </summary>
    public async Task<Result<string>> DeleteOrphanRecordsAsync(List<OrphanRecord> orphans)
    {
        if (orphans == null || orphans.Count == 0)
            return Result<string>.Fail("No orphan records provided.");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();

            await using var transaction = await db.Database.BeginTransactionAsync();
            int deleted = 0;

            try
            {
                var nozzleIds = orphans.Where(o => o.EntityType == "NozzleReading").Select(o => o.EntityId).ToList();
                if (nozzleIds.Count > 0)
                {
                    var rows = await db.Set<NozzleReading>().Where(n => nozzleIds.Contains(n.NozzleReadingId)).ToListAsync();
                    db.Set<NozzleReading>().RemoveRange(rows);
                    deleted += rows.Count;
                }

                var debitIds = orphans.Where(o => o.EntityType == "DebitEntry").Select(o => o.EntityId).ToList();
                if (debitIds.Count > 0)
                {
                    var rows = await db.Set<DebitEntry>().Where(d => debitIds.Contains(d.DebitId)).ToListAsync();
                    db.Set<DebitEntry>().RemoveRange(rows);
                    deleted += rows.Count;
                }

                var expenseIds = orphans.Where(o => o.EntityType == "Expense").Select(o => o.EntityId).ToList();
                if (expenseIds.Count > 0)
                {
                    var rows = await db.Set<Expense>().Where(e => expenseIds.Contains(e.ExpenseId)).ToListAsync();
                    db.Set<Expense>().RemoveRange(rows);
                    deleted += rows.Count;
                }

                var cashIds = orphans.Where(o => o.EntityType == "CashDenomination").Select(o => o.EntityId).ToList();
                if (cashIds.Count > 0)
                {
                    var rows = await db.Set<CashDenomination>().Where(c => cashIds.Contains(c.CashDenomId)).ToListAsync();
                    db.Set<CashDenomination>().RemoveRange(rows);
                    deleted += rows.Count;
                }

                var paymentIds = orphans.Where(o => o.EntityType == "PaymentCollection").Select(o => o.EntityId).ToList();
                if (paymentIds.Count > 0)
                {
                    var rows = await db.Set<PaymentCollection>().Where(p => paymentIds.Contains(p.PaymentId)).ToListAsync();
                    db.Set<PaymentCollection>().RemoveRange(rows);
                    deleted += rows.Count;
                }

                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                var msg = $"Deleted {deleted} orphan record(s).";
                _logger.Information(msg);
                return Result<string>.Ok(msg);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete orphan records");
            return Result<string>.Fail($"Delete failed: {ex.Message}");
        }
    }

    // ── DSM Entry Inspector & Repair Tools ────────────────────────────────────

    /// <summary>
    /// Fetches recent DSM entries for Developer Data Integrity Inspection with corruption analysis.
    /// Groups connected pump entries for the same DSM shift turn so totals/mismatches match.
    /// </summary>
    public async Task<List<RecentDsmEntryInspectorDto>> GetRecentDsmEntriesAsync(int limit = 100)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();

            var entries = await db.Set<DsmEntry>()
                .Include(e => e.Shift)
                .Include(e => e.PaymentCollection)
                .Include(e => e.NozzleReadings)
                .Include(e => e.DebitEntries)
                .Include(e => e.Expenses)
                .Include(e => e.TestingEntries)
                .Include(e => e.CashDenominations)
                .OrderByDescending(e => e.DsmEntryId)
                .Take(limit * 2)
                .ToListAsync();

            var processedEntryIds = new HashSet<int>();
            var result = new List<RecentDsmEntryInspectorDto>();

            foreach (var entry in entries)
            {
                if (processedEntryIds.Contains(entry.DsmEntryId))
                    continue;

                // Find connected slave entries (linked via ReconciledToPumpId, ConnectedPumpId, or same ShiftId + DsmName)
                var connectedEntries = entries.Where(e =>
                    !processedEntryIds.Contains(e.DsmEntryId) &&
                    e.DsmEntryId != entry.DsmEntryId &&
                    e.ShiftId == entry.ShiftId &&
                    string.Equals((e.DsmName ?? "").Trim(), (entry.DsmName ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
                    (
                        e.ReconciledToPumpId == entry.DsmEntryId ||
                        entry.ReconciledToPumpId == e.DsmEntryId ||
                        (entry.ConnectedPumpId.HasValue && e.PumpId == entry.ConnectedPumpId.Value) ||
                        (e.ConnectedPumpId.HasValue && entry.PumpId == e.ConnectedPumpId.Value) ||
                        e.ReconciledToPumpId.HasValue
                    )
                ).ToList();

                var group = new List<DsmEntry> { entry };
                group.AddRange(connectedEntries);

                foreach (var g in group)
                {
                    processedEntryIds.Add(g.DsmEntryId);
                }

                var entryIds = group.Select(g => g.DsmEntryId).OrderBy(id => id).ToList();
                var idDisplay = string.Join(", ", entryIds);

                var pumps = group.Select(g => $"Pump {g.PumpId}").Distinct().ToList();
                if (entry.ConnectedPumpId.HasValue)
                {
                    var connPumpLabel = $"Pump {entry.ConnectedPumpId.Value}";
                    if (!pumps.Contains(connPumpLabel)) pumps.Add(connPumpLabel);
                }
                var pumpLabel = string.Join(" & ", pumps);

                bool isPwa = group.Any(g => g.PaymentCollection != null && (!string.IsNullOrEmpty(g.PaymentCollection.PhonePeTid) || !string.IsNullOrEmpty(g.PaymentCollection.CardTid)));
                string origin = isPwa ? "PWA App" : "Desktop Manual";

                double totalGrossSales = 0;
                double totalCollection = 0;
                int totalNozzles = 0;
                int totalDebits = 0;
                int totalExpenses = 0;
                bool hasPaymentRecord = false;
                var issues = new List<string>();

                foreach (var g in group)
                {
                    totalNozzles += g.NozzleReadings?.Count ?? 0;
                    totalDebits += g.DebitEntries?.Count ?? 0;
                    totalExpenses += g.Expenses?.Count ?? 0;
                    if (g.PaymentCollection != null) hasPaymentRecord = true;

                    // Gross Sales
                    double entryGross = g.NozzleReadings != null && g.NozzleReadings.Count > 0
                        ? g.NozzleReadings.Sum(n => n.Amount)
                        : (double)g.GrossSales;
                    totalGrossSales += entryGross;

                    // Collection
                    double cash1 = g.CashDenominations?.Where(c => c.CashType == "Cash1").Sum(c => c.TotalAmount) ?? 0;
                    double cash2 = g.CashDenominations?.Where(c => c.CashType == "Cash2").Sum(c => c.TotalAmount) ?? 0;
                    double cashDeposit = cash1 > 0 ? cash1 : (g.PaymentCollection?.CashDeposit ?? 0);
                    double cashInHand = cash2;

                    var pc = g.PaymentCollection;
                    double digital = (pc?.PhonePe ?? 0) + (pc?.PhonePeMorning ?? 0) + (pc?.PhonePeDay ?? 0) + (pc?.PhonePeNight ?? 0)
                                   + (pc?.PhonePeCard ?? 0) + (pc?.PhonePeCardMorning ?? 0) + (pc?.PhonePeCardDay ?? 0) + (pc?.PhonePeCardNight ?? 0)
                                   + (pc?.CreditCardMorning ?? 0) + (pc?.CreditCardDay ?? 0) + (pc?.CreditCardNight ?? 0)
                                   + (pc?.PetroCard ?? 0) + (pc?.PetroCardMorning ?? 0) + (pc?.PetroCardDay ?? 0) + (pc?.PetroCardNight ?? 0);

                    double debits = g.DebitEntries?.Sum(d => d.Amount) ?? 0;
                    double expenses = g.Expenses?.Sum(ex => ex.Amount) ?? 0;
                    double testing = g.TestingEntries?.Sum(t => t.Amount) ?? 0;

                    totalCollection += (cashDeposit + cashInHand + digital + debits + expenses + testing);

                    // Check corruption per entry
                    if (g == entry && g.PaymentCollection == null && !g.ReconciledToPumpId.HasValue)
                    {
                        issues.Add("Missing Payment Record");
                    }
                    if (g.NozzleReadings == null || g.NozzleReadings.Count == 0)
                    {
                        issues.Add($"No Nozzle Readings (Pump {g.PumpId})");
                    }
                    else
                    {
                        foreach (var nr in g.NozzleReadings)
                        {
                            double expectedAmount = (nr.ClosingReading - nr.OpeningReading) * nr.Rate;
                            if (expectedAmount > 0 && Math.Abs(expectedAmount - nr.Amount) > 1.0)
                            {
                                issues.Add($"Nozzle Amount Calc Error on Pump {g.PumpId} Nozzle #{nr.NozzleNumber}");
                            }
                        }
                        if (g.NozzleReadings.Any(n => n.Rate <= 0 && n.ClosingReading > n.OpeningReading))
                        {
                            issues.Add($"Zero Fuel Rate (Pump {g.PumpId})");
                        }
                    }
                }

                var dto = new RecentDsmEntryInspectorDto
                {
                    IdDisplay = idDisplay,
                    EntryIds = entryIds,
                    ShiftId = entry.ShiftId,
                    ShiftDate = entry.Shift?.ShiftDate ?? entry.CreatedAt.Date,
                    ShiftType = entry.Shift?.ShiftType ?? "A",
                    DsmName = entry.DsmName ?? "Unknown",
                    PumpLabel = pumpLabel,
                    CreatedAt = entry.CreatedAt,
                    HasPaymentRecord = hasPaymentRecord,
                    NozzleCount = totalNozzles,
                    DebitCount = totalDebits,
                    ExpenseCount = totalExpenses,
                    GrossSales = totalGrossSales,
                    TotalCollection = totalCollection,
                    Origin = origin
                };

                if (issues.Count > 0)
                {
                    dto.HasCorruption = true;
                    dto.StatusMessage = string.Join("; ", issues.Distinct());
                }
                else
                {
                    dto.HasCorruption = false;
                    dto.StatusMessage = "OK";
                }

                result.Add(dto);
                if (result.Count >= limit) break;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get recent DSM entries for inspection");
            return new List<RecentDsmEntryInspectorDto>();
        }
    }

    /// <summary>
    /// Permanently deletes ANY DsmEntry record(s) and all associated child entities.
    /// </summary>
    public async Task<Result<string>> DeleteAnyDsmEntryAsync(IEnumerable<int> dsmEntryIds)
    {
        var idList = dsmEntryIds?.Distinct().Where(id => id > 0).ToList() ?? new List<int>();
        if (idList.Count == 0)
            return Result<string>.Fail("No valid DSM Entry IDs provided.");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();

            var entries = await db.Set<DsmEntry>()
                .Include(e => e.NozzleReadings)
                .Include(e => e.PaymentCollection)
                .Include(e => e.DebitEntries)
                .Include(e => e.TestingEntries)
                .Include(e => e.Expenses)
                .Include(e => e.CashDenominations)
                .Include(e => e.PersonalDebtors)
                .Include(e => e.KhandharePetroleumEntries)
                .Where(e => idList.Contains(e.DsmEntryId))
                .ToListAsync();

            if (entries.Count == 0)
                return Result<string>.Fail($"DsmEntry record(s) [{string.Join(", ", idList)}] not found in database.");

            await using var transaction = await db.Database.BeginTransactionAsync();

            try
            {
                int childCount = 0;

                foreach (var entry in entries)
                {
                    if (entry.NozzleReadings?.Count > 0)
                    {
                        db.Set<NozzleReading>().RemoveRange(entry.NozzleReadings);
                        childCount += entry.NozzleReadings.Count;
                    }

                    if (entry.DebitEntries?.Count > 0)
                    {
                        db.Set<DebitEntry>().RemoveRange(entry.DebitEntries);
                        childCount += entry.DebitEntries.Count;
                    }

                    if (entry.TestingEntries?.Count > 0)
                    {
                        db.Set<TestingEntry>().RemoveRange(entry.TestingEntries);
                        childCount += entry.TestingEntries.Count;
                    }

                    if (entry.Expenses?.Count > 0)
                    {
                        db.Set<Expense>().RemoveRange(entry.Expenses);
                        childCount += entry.Expenses.Count;
                    }

                    if (entry.CashDenominations?.Count > 0)
                    {
                        db.Set<CashDenomination>().RemoveRange(entry.CashDenominations);
                        childCount += entry.CashDenominations.Count;
                    }

                    if (entry.PersonalDebtors?.Count > 0)
                    {
                        db.Set<DsmPersonalDebtor>().RemoveRange(entry.PersonalDebtors);
                        childCount += entry.PersonalDebtors.Count;
                    }

                    if (entry.KhandharePetroleumEntries?.Count > 0)
                    {
                        db.Set<KhandharePetroleumEntry>().RemoveRange(entry.KhandharePetroleumEntries);
                        childCount += entry.KhandharePetroleumEntries.Count;
                    }

                    if (entry.PaymentCollection != null)
                    {
                        db.Set<PaymentCollection>().Remove(entry.PaymentCollection);
                        childCount++;
                    }

                    db.Set<DsmEntry>().Remove(entry);

                    db.Set<SyncChangeLog>().Add(new SyncChangeLog
                    {
                        TableName = "DsmEntries",
                        RecordId = entry.DsmEntryId,
                        Operation = "DELETE",
                        CreatedAt = DateTime.UtcNow,
                        IsSynced = false
                    });
                }

                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                var first = entries.First();
                var msg = $"Successfully deleted DsmEntry record(s) #{string.Join(", #", idList)} (DSM: {first.DsmName}) and {childCount} child records.";
                _logger.Information(msg);
                DsmEntryService.RaiseDsmEntryChanged();
                return Result<string>.Ok(msg);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete DsmEntry ids [{Ids}]", string.Join(", ", idList));
            return Result<string>.Fail($"Delete failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to repair structural corruptions in specified DsmEntry records.
    /// </summary>
    public async Task<Result<string>> FixCorruptedDsmEntryAsync(IEnumerable<int> dsmEntryIds)
    {
        var idList = dsmEntryIds?.Distinct().Where(id => id > 0).ToList() ?? new List<int>();
        if (idList.Count == 0)
            return Result<string>.Fail("No valid DSM Entry IDs provided.");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();

            var entries = await db.Set<DsmEntry>()
                .Include(e => e.NozzleReadings)
                .Include(e => e.PaymentCollection)
                .Include(e => e.DebitEntries)
                .Include(e => e.TestingEntries)
                .Include(e => e.Expenses)
                .Include(e => e.CashDenominations)
                .Where(e => idList.Contains(e.DsmEntryId))
                .ToListAsync();

            if (entries.Count == 0)
                return Result<string>.Fail($"DsmEntry ID(s) [{string.Join(", ", idList)}] not found.");

            var repairs = new List<string>();

            foreach (var entry in entries)
            {
                // 1. Repair missing PaymentCollection on non-reconciled entries
                if (entry.PaymentCollection == null && !entry.ReconciledToPumpId.HasValue)
                {
                    var pc = new PaymentCollection
                    {
                        DsmEntryId = entry.DsmEntryId,
                        PhonePeMorning = 0,
                        CreditCardMorning = 0,
                        PetroCardMorning = 0,
                        CashDeposit = 0,
                        Others = 0
                    };
                    db.Set<PaymentCollection>().Add(pc);
                    repairs.Add($"Created missing PaymentCollection for Entry #{entry.DsmEntryId}");
                }

                // 2. Repair Nozzle Readings amounts & GrossSales
                if (entry.NozzleReadings != null && entry.NozzleReadings.Count > 0)
                {
                    double calcGross = 0;
                    foreach (var nr in entry.NozzleReadings)
                    {
                        double expectedAmount = (nr.ClosingReading - nr.OpeningReading) * nr.Rate;
                        if (expectedAmount > 0 && Math.Abs(expectedAmount - nr.Amount) > 0.01)
                        {
                            nr.Amount = expectedAmount;
                            repairs.Add($"Corrected Nozzle #{nr.NozzleNumber} amount to ₹{expectedAmount:F2} on Entry #{entry.DsmEntryId}");
                        }
                        calcGross += nr.Amount;
                    }

                    if (!entry.ConnectedPumpId.HasValue && !entry.ReconciledToPumpId.HasValue)
                    {
                        if (Math.Abs(calcGross - (double)entry.GrossSales) > 0.01)
                        {
                            entry.GrossSales = (decimal)calcGross;
                            repairs.Add($"Updated GrossSales to ₹{calcGross:F2} on Entry #{entry.DsmEntryId}");
                        }
                    }
                }
            }

            if (repairs.Count == 0)
            {
                return Result<string>.Ok($"DsmEntry record(s) #{string.Join(", #", idList)} checked — no structural corruptions found.");
            }

            await db.SaveChangesAsync();
            var summary = $"Repaired DsmEntry record(s) #{string.Join(", #", idList)}: " + string.Join(", ", repairs);
            _logger.Information(summary);
            DsmEntryService.RaiseDsmEntryChanged();
            return Result<string>.Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to repair DsmEntry ids [{Ids}]", string.Join(", ", idList));
            return Result<string>.Fail($"Repair failed: {ex.Message}");
        }
    }
}

public class RecentDsmEntryInspectorDto
{
    public string IdDisplay { get; set; } = string.Empty;
    public List<int> EntryIds { get; set; } = new();
    public int PrimaryDsmEntryId => EntryIds.FirstOrDefault();
    public int ShiftId { get; set; }
    public DateTime ShiftDate { get; set; }
    public string ShiftType { get; set; } = string.Empty;
    public string DsmName { get; set; } = string.Empty;
    public string PumpLabel { get; set; } = string.Empty;
    public string Origin { get; set; } = "Desktop Manual";
    public double GrossSales { get; set; }
    public double TotalCollection { get; set; }
    public double Mismatch => TotalCollection - GrossSales;
    public DateTime CreatedAt { get; set; }
    public bool HasPaymentRecord { get; set; }
    public int NozzleCount { get; set; }
    public int DebitCount { get; set; }
    public int ExpenseCount { get; set; }
    public string StatusMessage { get; set; } = "OK";
    public bool HasCorruption { get; set; }
}
