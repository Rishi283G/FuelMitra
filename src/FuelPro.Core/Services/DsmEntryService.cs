using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.DTOs;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FuelPro.Core.Services;

/// <summary>
/// Orchestrates complete DSM entry save/load with transactions.
/// </summary>
public class DsmEntryService
{
    private readonly IDsmEntryRepository _dsmRepo;
    private readonly INozzleReadingRepository _nozzleRepo;
    private readonly IPaymentRepository _paymentRepo;
    private readonly IDebitEntryRepository _debitRepo;
    private readonly ITestingEntryRepository _testingRepo;
    private readonly IExpenseRepository _expenseRepo;
    private readonly ICashDenominationRepository _cashRepo;
    private readonly IShiftRepository _shiftRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IDsmCalculationService _dsmCalculationService;
    private readonly ILogger _logger = Log.ForContext<DsmEntryService>();

    public DsmEntryService(
        IDsmEntryRepository dsmRepo,
        INozzleReadingRepository nozzleRepo,
        IPaymentRepository paymentRepo,
        IDebitEntryRepository debitRepo,
        ITestingEntryRepository testingRepo,
        IExpenseRepository expenseRepo,
        ICashDenominationRepository cashRepo,
        IShiftRepository shiftRepo,
        ISettingsRepository settingsRepo,
        IDsmCalculationService dsmCalculationService)
    {
        _dsmRepo = dsmRepo;
        _nozzleRepo = nozzleRepo;
        _paymentRepo = paymentRepo;
        _debitRepo = debitRepo;
        _testingRepo = testingRepo;
        _expenseRepo = expenseRepo;
        _cashRepo = cashRepo;
        _shiftRepo = shiftRepo;
        _settingsRepo = settingsRepo;
        _dsmCalculationService = dsmCalculationService;
    }

    /// <summary>
    /// Saves a complete DSM entry with all child data.
    /// </summary>
    public async Task<Result<DsmEntry>> SaveCompleteEntryAsync(
        DateTime date, string shiftType, string dsmName, int pumpId,
        List<NozzleReading> nozzleReadings,
        PaymentCollection payment,
        List<DebitEntry> debits,
        List<TestingEntry> testingEntries,
        List<Expense> expenses,
        List<CashDenomination> cashDenominations,
        int? connectedPumpId = null,
        int? existingEntryId = null)
    {
        try
        {
            // Check shift lock
            var shiftResult = await _shiftRepo.GetOrCreateShiftAsync(date, shiftType);
            if (!shiftResult.Success) return Result<DsmEntry>.Fail(shiftResult.Error);
            var shift = shiftResult.Data!;

            if (shift.IsLocked)
                return Result<DsmEntry>.Fail("This shift is locked and cannot be edited.");

            // Check duplicate — but if the existing entry is an orphan from a
            // previously failed save (no PaymentCollection), treat it as the
            // entry to update rather than blocking.
            if (existingEntryId == null)
            {
                var dupResult = await _dsmRepo.IsDuplicateAsync(shift.ShiftId, pumpId, dsmName);
                if (dupResult.Success && dupResult.Data)
                {
                    // Look up the existing entry to see if it's an orphan
                    var existingEntries = await _dsmRepo.GetEntriesForShiftAsync(shift.ShiftId);
                    var orphan = existingEntries.Data?.FirstOrDefault(e =>
                        e.PumpId == pumpId &&
                        string.Equals(e.DsmName, dsmName, StringComparison.OrdinalIgnoreCase) &&
                        e.PaymentCollection == null);

                    if (orphan != null)
                    {
                        // This is an orphan from a failed save — reuse it
                        _logger.Information("Found orphaned DSM entry {Id} for {Dsm}/Pump {Pump}, reusing",
                            orphan.DsmEntryId, dsmName, pumpId);
                        existingEntryId = orphan.DsmEntryId;
                    }
                    else
                    {
                        return Result<DsmEntry>.Fail($"A DSM entry for '{dsmName}' on Pump {pumpId} already exists in this shift.");
                    }
                }
            }

            // Create or update DSM entry
            var entry = new DsmEntry
            {
                DsmEntryId = existingEntryId ?? 0,
                ShiftId = shift.ShiftId,
                DsmName = dsmName,
                PumpId = pumpId,
                ConnectedPumpId = connectedPumpId
            };

            var saveResult = await _dsmRepo.SaveEntryAsync(entry);
            if (!saveResult.Success) return saveResult;
            var savedEntry = saveResult.Data!;

            // Save all child data
            var nozzleResult = await _nozzleRepo.SaveReadingsAsync(savedEntry.DsmEntryId, nozzleReadings);
            if (!nozzleResult.Success) return Result<DsmEntry>.Fail(nozzleResult.Error);

            payment.DsmEntryId = savedEntry.DsmEntryId;
            var paymentResult = await _paymentRepo.SavePaymentAsync(payment);
            if (!paymentResult.Success) return Result<DsmEntry>.Fail(paymentResult.Error);

            var debitResult = await _debitRepo.SaveDebitsAsync(savedEntry.DsmEntryId, debits);
            if (!debitResult.Success) return Result<DsmEntry>.Fail(debitResult.Error);

            var testingResult = await _testingRepo.SaveTestingEntriesAsync(savedEntry.DsmEntryId, testingEntries);
            if (!testingResult.Success) return Result<DsmEntry>.Fail(testingResult.Error);

            var expenseResult = await _expenseRepo.SaveExpensesAsync(savedEntry.DsmEntryId, expenses);
            if (!expenseResult.Success) return Result<DsmEntry>.Fail(expenseResult.Error);

            var cashResult = await _cashRepo.SaveCashDenominationsAsync(savedEntry.DsmEntryId, cashDenominations);
            if (!cashResult.Success) return Result<DsmEntry>.Fail(cashResult.Error);

            // Re-load full entry and persist canonical totals.
            var fullResult = await _dsmRepo.GetFullEntryAsync(savedEntry.DsmEntryId);
            if (fullResult.Success && fullResult.Data != null)
            {
                var calc = _dsmCalculationService.Calculate(ToCalculationDto(fullResult.Data));
                savedEntry.GrossSales = calc.GrossSales;
                savedEntry.TotalInDirect = calc.TotalInDirect;
                savedEntry.TotalCreditors = calc.TotalCreditors;
                savedEntry.TotalCollection = calc.TotalCollection;
                savedEntry.Mismatch = calc.Mismatch;
                await _dsmRepo.SaveEntryAsync(savedEntry);
            }

            // Keep pairing metadata in sync so second-pump entries are excluded from mismatch/short reporting.
            var shiftEntriesResult = await _dsmRepo.GetEntriesForShiftAsync(shift.ShiftId);
            if (shiftEntriesResult.Success && shiftEntriesResult.Data != null)
            {
                var shiftEntries = shiftEntriesResult.Data;
                var firstEntry = shiftEntries.FirstOrDefault(e => e.DsmEntryId == savedEntry.DsmEntryId);
                if (firstEntry != null)
                {
                    firstEntry.ConnectedPumpId = connectedPumpId;
                    await _dsmRepo.SaveEntryAsync(firstEntry);
                }

                foreach (var candidate in shiftEntries.Where(e =>
                             e.DsmEntryId != savedEntry.DsmEntryId
                             && string.Equals(e.DsmName, dsmName, StringComparison.OrdinalIgnoreCase)))
                {
                    // This entry is the "second pump" for the current one.
                    if (connectedPumpId.HasValue && candidate.PumpId == connectedPumpId.Value)
                    {
                        candidate.ReconciledToPumpId = pumpId;
                        await _dsmRepo.SaveEntryAsync(candidate);
                    }

                    // If this current save is actually the second entry, detect existing first-entry link.
                    if (candidate.ConnectedPumpId == pumpId)
                    {
                        var current = shiftEntries.FirstOrDefault(e => e.DsmEntryId == savedEntry.DsmEntryId);
                        if (current != null)
                        {
                            current.ReconciledToPumpId = candidate.PumpId;
                            await _dsmRepo.SaveEntryAsync(current);
                        }
                    }
                }
            }

            _logger.Information("DSM entry saved successfully: {DsmName} Pump {PumpId}", dsmName, pumpId);
            return Result<DsmEntry>.Ok(savedEntry);
        }
        catch (DbUpdateException dbEx)
        {
            var innerMsg = dbEx.InnerException?.Message ?? dbEx.Message;
            _logger.Error(dbEx, "DbUpdateException saving DSM entry. Inner: {InnerMessage}", innerMsg);
            return Result<DsmEntry>.Fail($"Database save failed: {innerMsg}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save complete DSM entry");
            return Result<DsmEntry>.Fail($"Failed to save DSM entry: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets current fuel rates from settings.
    /// </summary>
    public async Task<(double hsdRate, double msIRate, double msIIRate)> GetCurrentRatesAsync()
    {
        var result = await _settingsRepo.GetSettingsAsync();
        if (result.Success && result.Data != null)
            return (result.Data.HsdRate, result.Data.MsIRate, result.Data.MsIIRate);
        return (90.35, 103.81, 103.81);
    }

    /// <summary>
    /// Gets all DSM entries for a shift as summary DTOs.
    /// </summary>
    public async Task<Result<List<DsmEntrySummaryDto>>> GetShiftEntrySummariesAsync(int shiftId)
    {
        try
        {
            var entriesResult = await _dsmRepo.GetEntriesForShiftAsync(shiftId);
            if (!entriesResult.Success) return Result<List<DsmEntrySummaryDto>>.Fail(entriesResult.Error);

            var summaries = entriesResult.Data!.Select(e =>
            {
                var calc = _dsmCalculationService.Calculate(ToCalculationDto(e));

                return new DsmEntrySummaryDto
                {
                    DsmEntryId = e.DsmEntryId,
                    DsmName = e.DsmName,
                    PumpId = e.PumpId,
                    GrossSales = (double)calc.GrossSales,
                    TotalPaymentIn = (double)calc.TotalCollection,
                    Difference = e.ReconciledToPumpId.HasValue ? 0 : (double)calc.Mismatch,
                    CreatedAt = e.CreatedAt
                };
            }).ToList();

            return Result<List<DsmEntrySummaryDto>>.Ok(summaries);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get shift entry summaries");
            return Result<List<DsmEntrySummaryDto>>.Fail($"Failed to load summaries: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets autocomplete DSM names from previous entries.
    /// </summary>
    public async Task<List<string>> GetDsmNameSuggestionsAsync()
    {
        var result = await _dsmRepo.GetDistinctDsmNamesAsync();
        return result.Success ? result.Data! : new List<string>();
    }

    private static DsmEntryDto ToCalculationDto(DsmEntry entry)
    {
        var cash1Total = entry.CashDenominations.Where(c => c.CashType == "Cash1").Sum(c => c.TotalAmount);
        var cash2Total = entry.CashDenominations.Where(c => c.CashType == "Cash2").Sum(c => c.TotalAmount);
        return new DsmEntryDto
        {
            DSMEntryId = entry.DsmEntryId,
            NozzleReadings = entry.NozzleReadings.Select(n => new NozzleReadingDto { Amount = (decimal)n.Amount }).ToList(),
            PaymentCollection = new PaymentCollectionDto
            {
                // Others is NOT included in TotalInDirect — it is informational only
                PhonePe      = (decimal)((entry.PaymentCollection?.PhonePe ?? 0)
                                       + (entry.PaymentCollection?.PhonePeCard ?? 0)),
                CreditCard   = (decimal)((entry.PaymentCollection?.CreditCard ?? 0) + (entry.PaymentCollection?.PetroCard ?? 0)),
                CashDeposit  = (decimal)(entry.PaymentCollection?.CashDeposit ?? 0),
                PhysicalCash = (decimal)(cash1Total + cash2Total)
            },
            DebitEntries   = entry.DebitEntries.Select(d => new DebitEntryDto { Amount = (decimal)d.Amount }).ToList(),
            Expenses       = entry.Expenses.Select(e => new ExpenseDto { Amount = (decimal)e.Amount }).ToList(),
            TestingEntries = entry.TestingEntries.Select(t => new TestingEntryDto
            {
                FuelType = t.FuelType,
                Litres = (decimal)t.Litres,
                Rate = (decimal)t.Rate,
                Amount = (decimal)t.Amount
            }).ToList()
        };
    }
}
