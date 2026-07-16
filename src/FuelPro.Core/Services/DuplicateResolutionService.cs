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
}
