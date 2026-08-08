using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FuelPro.Core.Services;

/// <summary>
/// Orchestrates complete DSM entry save/load with transactions.
/// </summary>
public class DsmEntryService
{
    public static event Action? DsmEntryChanged;
    public static void RaiseDsmEntryChanged() => DsmEntryChanged?.Invoke();

    public static event Action? PettyCashChanged;
    public static void RaisePettyCashChanged() => PettyCashChanged?.Invoke();

    public static event Action? InventoryChanged;
    public static void RaiseInventoryChanged() => InventoryChanged?.Invoke();

    public static event Action? DebtorChanged;
    public static void RaiseDebtorChanged() => DebtorChanged?.Invoke();

    public static event Action? PayrollChanged;
    public static void RaisePayrollChanged() => PayrollChanged?.Invoke();

    public static event Action? SettingsChanged;
    public static void RaiseSettingsChanged() => SettingsChanged?.Invoke();

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
    private readonly IDsmPersonalDebtorRepository _personalDebtorRepo;
    private readonly IServiceProvider _serviceProvider;
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
        IDsmCalculationService dsmCalculationService,
        IDsmPersonalDebtorRepository personalDebtorRepo,
        IServiceProvider serviceProvider)
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
        _personalDebtorRepo = personalDebtorRepo;
        _serviceProvider = serviceProvider;
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
        int? existingEntryId = null,
        string? startTime = null,
        string? endTime = null,
        List<DsmPersonalDebtor>? personalDebtors = null,
        List<KhandharePetroleumEntry>? khandharePetroleumEntries = null)
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
            // Pre-calculate nozzle readings SaleLitres and Amount in memory so they are available for gross sales calculations
            foreach (var nr in nozzleReadings)
            {
                nr.SaleLitres = nr.ClosingReading - nr.OpeningReading;
                nr.Amount = nr.SaleLitres * nr.Rate;
            }

            // Split nozzle readings into primary and connected pump nozzles
            var primaryReadings = new List<NozzleReading>();
            var connectedReadings = new List<NozzleReading>();
            foreach (var nr in nozzleReadings)
            {
                var nozzlePumpId = PumpConfiguration.GetPumpIdForNozzle(nr.NozzleNumber, date);
                if (nozzlePumpId == 0) nozzlePumpId = pumpId;

                if (connectedPumpId.HasValue && nozzlePumpId == connectedPumpId.Value)
                {
                    connectedReadings.Add(nr);
                }
                else
                {
                    primaryReadings.Add(nr);
                }
            }

            // Check duplicate for primary — three outcomes:
            //   1. No duplicate → proceed with insert.
            //   2. Orphan (no PaymentCollection) from a failed save → reuse it.
            //   3. Fully-approved entry already exists → return it as-is (idempotent).
            if (existingEntryId == null)
            {
                var dupResult = await _dsmRepo.IsDuplicateAsync(shift.ShiftId, pumpId, dsmName);
                if (dupResult.Success && dupResult.Data)
                {
                    var existingEntries = await _dsmRepo.GetEntriesForShiftAsync(shift.ShiftId);
                    var matchingEntry = existingEntries.Data?.FirstOrDefault(e =>
                        e.PumpId == pumpId &&
                        string.Equals(e.DsmName, dsmName, StringComparison.OrdinalIgnoreCase));

                    if (matchingEntry == null)
                    {
                        return Result<DsmEntry>.Fail($"A DSM entry for '{dsmName}' on Pump {pumpId} already exists in this shift.");
                    }
                    else if (matchingEntry.PaymentCollection == null)
                    {
                        // Orphan from a failed save — reuse it
                        _logger.Information("Found orphaned DSM entry {Id} for {Dsm}/Pump {Pump}, reusing",
                            matchingEntry.DsmEntryId, dsmName, pumpId);
                        existingEntryId = matchingEntry.DsmEntryId;
                    }
                    else
                    {
                        // Fully-approved entry already present — return it unchanged (idempotent re-approval)
                        _logger.Warning(
                            "Idempotent save: DsmEntry {Id} for {Dsm}/Pump {Pump} is already fully approved. Returning existing record.",
                            matchingEntry.DsmEntryId, dsmName, pumpId);
                        var fullEntry = await _dsmRepo.GetFullEntryAsync(matchingEntry.DsmEntryId);
                        return fullEntry.Success
                            ? Result<DsmEntry>.Ok(fullEntry.Data!)
                            : Result<DsmEntry>.Ok(matchingEntry);
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
                ConnectedPumpId = connectedPumpId,
                StartTime = startTime,
                EndTime = endTime
            };

            var saveResult = await _dsmRepo.SaveEntryAsync(entry);
            if (!saveResult.Success) return saveResult;
            var savedEntry = saveResult.Data!;

            // Save all child data for primary entry
            var nozzleResult = await _nozzleRepo.SaveReadingsAsync(savedEntry.DsmEntryId, primaryReadings);
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

            var personalDebtorResult = await _personalDebtorRepo.SavePersonalDebtorsAsync(savedEntry.DsmEntryId, personalDebtors ?? new List<DsmPersonalDebtor>());
            if (!personalDebtorResult.Success) return Result<DsmEntry>.Fail(personalDebtorResult.Error);

            // Save Khandhare Petroleum Entries
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
                var existingKP = await dbContext.Set<KhandharePetroleumEntry>().Where(kp => kp.DsmEntryId == savedEntry.DsmEntryId).ToListAsync();
                dbContext.Set<KhandharePetroleumEntry>().RemoveRange(existingKP);

                if (khandharePetroleumEntries != null && khandharePetroleumEntries.Count > 0)
                {
                    foreach (var kp in khandharePetroleumEntries)
                    {
                        kp.DsmEntryId = savedEntry.DsmEntryId;
                        kp.DsmEntry = null;
                        kp.DsmName = dsmName;
                        kp.Date = shift.ShiftDate;
                    }
                    dbContext.Set<KhandharePetroleumEntry>().AddRange(khandharePetroleumEntries);
                }
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save Khandhare Petroleum entries for DsmEntryId {DsmEntryId}", savedEntry.DsmEntryId);
            }

            // Re-load full entry and persist canonical totals.
            var fullResult = await _dsmRepo.GetFullEntryAsync(savedEntry.DsmEntryId);
            if (fullResult.Success && fullResult.Data != null)
            {
                var calc = _dsmCalculationService.Calculate(ToCalculationDto(fullResult.Data));
                var connectedGross = connectedPumpId.HasValue ? (decimal)connectedReadings.Sum(x => x.Amount) : 0m;
                savedEntry.GrossSales = calc.GrossSales + connectedGross;
                savedEntry.TotalInDirect = calc.TotalInDirect;
                savedEntry.TotalCreditors = calc.TotalCreditors;
                savedEntry.TotalCollection = calc.TotalCollection;
                savedEntry.Mismatch = calc.TotalCollection - savedEntry.GrossSales;
                await _dsmRepo.SaveEntryAsync(savedEntry);
            }

            // Save the connected pump entry if one is specified
            if (connectedPumpId.HasValue)
            {
                var shiftEntriesResult = await _dsmRepo.GetEntriesForShiftAsync(shift.ShiftId);
                DsmEntry? existingConnectedEntry = null;
                if (shiftEntriesResult.Success && shiftEntriesResult.Data != null)
                {
                    existingConnectedEntry = shiftEntriesResult.Data.FirstOrDefault(e =>
                        e.PumpId == connectedPumpId.Value
                        && string.Equals(e.DsmName, dsmName, StringComparison.OrdinalIgnoreCase));
                }

                var connectedEntry = new DsmEntry
                {
                    DsmEntryId = existingConnectedEntry?.DsmEntryId ?? 0,
                    ShiftId = shift.ShiftId,
                    DsmName = dsmName,
                    PumpId = connectedPumpId.Value,
                    ReconciledToPumpId = pumpId,
                    StartTime = startTime,
                    EndTime = endTime
                };

                var saveConnResult = await _dsmRepo.SaveEntryAsync(connectedEntry);
                if (saveConnResult.Success)
                {
                    var savedConnectedEntry = saveConnResult.Data!;
                    await _nozzleRepo.SaveReadingsAsync(savedConnectedEntry.DsmEntryId, connectedReadings);
                    
                    var connPayment = new PaymentCollection { DsmEntryId = savedConnectedEntry.DsmEntryId };
                    await _paymentRepo.SavePaymentAsync(connPayment);

                    await _debitRepo.SaveDebitsAsync(savedConnectedEntry.DsmEntryId, new List<DebitEntry>());
                    await _testingRepo.SaveTestingEntriesAsync(savedConnectedEntry.DsmEntryId, new List<TestingEntry>());
                    await _expenseRepo.SaveExpensesAsync(savedConnectedEntry.DsmEntryId, new List<Expense>());
                    await _cashRepo.SaveCashDenominationsAsync(savedConnectedEntry.DsmEntryId, new List<CashDenomination>());

                    var connFullResult = await _dsmRepo.GetFullEntryAsync(savedConnectedEntry.DsmEntryId);
                    if (connFullResult.Success && connFullResult.Data != null)
                    {
                        var calc = _dsmCalculationService.Calculate(ToCalculationDto(connFullResult.Data));
                        savedConnectedEntry.GrossSales = calc.GrossSales;
                        savedConnectedEntry.TotalInDirect = calc.TotalInDirect;
                        savedConnectedEntry.TotalCreditors = calc.TotalCreditors;
                        savedConnectedEntry.TotalCollection = calc.TotalCollection;
                        savedConnectedEntry.Mismatch = 0m; // Connected entry mismatch is always 0 because collections are in primary
                        await _dsmRepo.SaveEntryAsync(savedConnectedEntry);
                    }
                }
            }
            else
            {
                // Clear any existing ReconciledToPumpId links for this primary pump in the shift
                var shiftEntriesResult = await _dsmRepo.GetEntriesForShiftAsync(shift.ShiftId);
                if (shiftEntriesResult.Success && shiftEntriesResult.Data != null)
                {
                    foreach (var candidate in shiftEntriesResult.Data)
                    {
                        if (candidate.ReconciledToPumpId == pumpId && string.Equals(candidate.DsmName, dsmName, StringComparison.OrdinalIgnoreCase))
                        {
                            candidate.ReconciledToPumpId = null;
                            await _dsmRepo.SaveEntryAsync(candidate);
                        }
                    }
                }
            }

            // Sync pairing properties for other entries if relevant
            var finalShiftEntriesResult = await _dsmRepo.GetEntriesForShiftAsync(shift.ShiftId);
            if (finalShiftEntriesResult.Success && finalShiftEntriesResult.Data != null)
            {
                var shiftEntries = finalShiftEntriesResult.Data;
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
                    if (connectedPumpId.HasValue && candidate.PumpId == connectedPumpId.Value)
                    {
                        candidate.ReconciledToPumpId = pumpId;
                        await _dsmRepo.SaveEntryAsync(candidate);
                    }

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

            // Propagate nozzle readings downstream
            await PropagateNozzleReadingsAsync(date, shiftType);

            _logger.Information("DSM entry saved successfully: {DsmName} Pump {PumpId}", dsmName, pumpId);
            RaiseDsmEntryChanged();
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
    public async Task<(double hsdRate, double msIRate, double msIIRate, double cngRate)> GetCurrentRatesAsync()
    {
        var result = await _settingsRepo.GetSettingsAsync();
        if (result.Success && result.Data != null)
            return (result.Data.HsdRate, result.Data.MsIRate, result.Data.MsIIRate, result.Data.CngRate);
        return (90.35, 103.81, 103.81, 85.0);
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

            var summaries = entriesResult.Data!
                .Where(e => !e.ReconciledToPumpId.HasValue)
                .Select(e => new DsmEntrySummaryDto
                {
                    DsmEntryId = e.DsmEntryId,
                    DsmName = e.DsmName,
                    PumpId = e.PumpId,
                    ConnectedPumpId = e.ConnectedPumpId,
                    ReconciledToPumpId = e.ReconciledToPumpId,
                    GrossSales = (double)e.GrossSales,
                    TotalPaymentIn = (double)e.TotalCollection,
                    Difference = e.ReconciledToPumpId.HasValue ? 0 : (double)e.Mismatch,
                    CreatedAt = e.CreatedAt
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
                PhonePe      = (decimal)((entry.PaymentCollection?.PhonePeMorning ?? 0)
                                       + (entry.PaymentCollection?.PhonePeDay ?? 0)
                                       + (entry.PaymentCollection?.PhonePeNight ?? 0)
                                       + (entry.PaymentCollection?.PhonePeCardMorning ?? 0)
                                       + (entry.PaymentCollection?.PhonePeCardDay ?? 0)
                                       + (entry.PaymentCollection?.PhonePeCardNight ?? 0)),
                CreditCard   = (decimal)((entry.PaymentCollection?.CreditCardMorning ?? 0)
                                       + (entry.PaymentCollection?.CreditCardDay ?? 0)
                                       + (entry.PaymentCollection?.CreditCardNight ?? 0)),
                PetroCard    = (decimal)((entry.PaymentCollection?.PetroCardMorning ?? 0)
                                       + (entry.PaymentCollection?.PetroCardDay ?? 0)
                                       + (entry.PaymentCollection?.PetroCardNight ?? 0)),
                CashDeposit  = (decimal)(cash1Total > 0 ? cash1Total : (entry.PaymentCollection?.CashDeposit ?? 0)),
                PhysicalCash = (decimal)cash2Total
            },
            DebitEntries   = entry.DebitEntries.Select(d => new DebitEntryDto { Amount = (decimal)d.Amount }).ToList(),
            Expenses       = entry.Expenses.Select(e => new ExpenseDto { Amount = (decimal)e.Amount })
                                .Concat(entry.KhandharePetroleumEntries?.Select(kp => new ExpenseDto { Amount = (decimal)kp.Amount }) ?? Array.Empty<ExpenseDto>())
                                .ToList(),
            TestingEntries = entry.TestingEntries.Select(t => new TestingEntryDto
            {
                FuelType = t.FuelType,
                Litres = (decimal)t.Litres,
                Rate = (decimal)t.Rate,
                Amount = (decimal)t.Amount
            }).ToList()
        };
    }

    public async Task<Result> PropagateNozzleReadingsAsync(DateTime startDate, string startShiftType)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var shiftRepo = scope.ServiceProvider.GetRequiredService<IShiftRepository>();
            var dsmRepo = scope.ServiceProvider.GetRequiredService<IDsmEntryRepository>();
            var nozzleRepo = scope.ServiceProvider.GetRequiredService<INozzleReadingRepository>();

            // Load shifts from startDate onwards (up to 30 days forward to keep it bounded)
            var shiftsResult = await shiftRepo.GetShiftsByDateRangeAsync(startDate.Date, startDate.Date.AddDays(30));
            if (!shiftsResult.Success || shiftsResult.Data == null) return Result.Ok();

            // Sort operationally: Date ascending, then Shift B (Day) before Shift A (Night)
            var sortedShifts = shiftsResult.Data
                .OrderBy(s => s.ShiftDate)
                .ThenBy(s => s.ShiftType == "A")
                .ToList();

            var startIdx = sortedShifts.FindIndex(s => s.ShiftDate.Date == startDate.Date && s.ShiftType == startShiftType);
            if (startIdx < 0) return Result.Ok();

            // Build a map of previous nozzle closings from the shift before startIdx
            var prevClosings = new Dictionary<int, double>();
            if (startIdx > 0)
            {
                var prevShift = sortedShifts[startIdx - 1];
                var prevEntriesResult = await dsmRepo.GetEntriesForShiftAsync(prevShift.ShiftId);
                if (prevEntriesResult.Success && prevEntriesResult.Data != null)
                {
                    foreach (var entry in prevEntriesResult.Data)
                    {
                        foreach (var r in entry.NozzleReadings)
                        {
                            prevClosings[r.NozzleNumber] = r.ClosingReading;
                        }
                    }
                }
            }

            for (int i = startIdx; i < sortedShifts.Count; i++)
            {
                var currentShift = sortedShifts[i];
                var entriesResult = await dsmRepo.GetEntriesForShiftAsync(currentShift.ShiftId);
                if (!entriesResult.Success || entriesResult.Data == null) continue;

                foreach (var entry in entriesResult.Data)
                {
                    bool entryChanged = false;
                    var updatedReadings = new List<NozzleReading>();

                    foreach (var reading in entry.NozzleReadings)
                    {
                        double newOpening = reading.OpeningReading;

                        if (prevClosings.TryGetValue(reading.NozzleNumber, out var prevClosing))
                        {
                            newOpening = prevClosing;
                            if (newOpening > reading.ClosingReading)
                            {
                                newOpening = reading.ClosingReading;
                                _logger.Warning("Nozzle {NozzleNumber} prev closing {PrevClosing} > current closing {CurrentClosing} in {Date} {Shift}. Capping.",
                                    reading.NozzleNumber, prevClosing, reading.ClosingReading, currentShift.ShiftDate, currentShift.ShiftType);
                            }
                        }

                        if (Math.Abs(reading.OpeningReading - newOpening) > 0.001)
                        {
                            reading.OpeningReading = newOpening;
                            reading.SaleLitres = reading.ClosingReading - reading.OpeningReading;
                            reading.Amount = reading.SaleLitres * reading.Rate;
                            entryChanged = true;
                        }

                        updatedReadings.Add(reading);
                    }

                    if (entryChanged)
                    {
                        await nozzleRepo.SaveReadingsAsync(entry.DsmEntryId, updatedReadings);

                        // Re-load and recalculate totals
                        var fullResult = await dsmRepo.GetFullEntryAsync(entry.DsmEntryId);
                        if (fullResult.Success && fullResult.Data != null)
                        {
                            var calc = _dsmCalculationService.Calculate(ToCalculationDto(fullResult.Data));
                            var connectedGross = entry.ReconciledToPumpId.HasValue ? 0m : 0m;
                            entry.GrossSales = calc.GrossSales;
                            entry.TotalInDirect = calc.TotalInDirect;
                            entry.TotalCreditors = calc.TotalCreditors;
                            entry.TotalCollection = calc.TotalCollection;
                            entry.Mismatch = calc.TotalCollection - entry.GrossSales;
                            await dsmRepo.SaveEntryAsync(entry);
                        }
                    }

                    // Update prevClosings map with this entry's nozzle closings
                    foreach (var r in updatedReadings)
                    {
                        prevClosings[r.NozzleNumber] = r.ClosingReading;
                    }
                }
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to propagate nozzle readings starting from {Date} {Shift}", startDate, startShiftType);
            return Result.Fail($"Failed to propagate nozzle readings: {ex.Message}");
        }
    }

    public async Task<Result<DsmEntry>> SaveCompleteEntryWithContextAsync(
        DbContext context,
        DateTime date, string shiftType, string dsmName, int pumpId,
        List<NozzleReading> nozzleReadings,
        PaymentCollection payment,
        List<DebitEntry> debits,
        List<TestingEntry> testingEntries,
        List<Expense> expenses,
        List<CashDenomination> cashDenominations,
        int? connectedPumpId = null,
        int? existingEntryId = null,
        string? startTime = null,
        string? endTime = null,
        List<DsmPersonalDebtor>? personalDebtors = null,
        List<KhandharePetroleumEntry>? khandharePetroleumEntries = null)
    {
        try
        {
            var dateOnly = date.Date;
            var altShift = shiftType == "A" ? "I" : (shiftType == "B" ? "II" : (shiftType == "C" ? "III" : (shiftType == "I" ? "A" : (shiftType == "II" ? "B" : (shiftType == "III" ? "C" : shiftType)))));
            
            // Check/Get/Create Shift
            var shift = await context.Set<Shift>()
                .FirstOrDefaultAsync(s => s.ShiftDate == dateOnly && (s.ShiftType == shiftType || s.ShiftType == altShift));
            if (shift == null)
            {
                shift = new Shift
                {
                    ShiftDate = dateOnly,
                    ShiftType = shiftType,
                    IsLocked = false,
                    CreatedAt = DateTime.Now
                };
                context.Set<Shift>().Add(shift);
                await context.SaveChangesAsync();
            }

            if (shift.IsLocked)
                return Result<DsmEntry>.Fail("This shift is locked and cannot be edited.");

            // Pre-calculate nozzle readings SaleLitres and Amount
            foreach (var nr in nozzleReadings)
            {
                nr.SaleLitres = nr.ClosingReading - nr.OpeningReading;
                nr.Amount = nr.SaleLitres * nr.Rate;
            }

            // Split nozzle readings into primary and connected pump nozzles
            var primaryReadings = new List<NozzleReading>();
            var connectedReadings = new List<NozzleReading>();
            foreach (var nr in nozzleReadings)
            {
                var nozzlePumpId = PumpConfiguration.GetPumpIdForNozzle(nr.NozzleNumber, date);
                if (nozzlePumpId == 0) nozzlePumpId = pumpId;

                if (connectedPumpId.HasValue && nozzlePumpId == connectedPumpId.Value)
                {
                    connectedReadings.Add(nr);
                }
                else
                {
                    primaryReadings.Add(nr);
                }
            }

            // Check duplicate for primary — three outcomes:
            //   1. No duplicate → proceed with insert.
            //   2. Orphan (no PaymentCollection) from a failed save → reuse it.
            //   3. Fully-approved entry already exists → return it as-is (idempotent).
            if (existingEntryId == null)
            {
                var existingMatch = await context.Set<DsmEntry>()
                    .Include(e => e.PaymentCollection)
                    .FirstOrDefaultAsync(e =>
                        e.ShiftId == shift.ShiftId &&
                        e.PumpId == pumpId &&
                        e.DsmName == dsmName);

                if (existingMatch != null)
                {
                    if (existingMatch.PaymentCollection == null)
                    {
                        // Orphan from a failed save — reuse it
                        _logger.Information("Found orphaned DSM entry {Id} for {Dsm}/Pump {Pump}, reusing",
                            existingMatch.DsmEntryId, dsmName, pumpId);
                        existingEntryId = existingMatch.DsmEntryId;
                    }
                    else
                    {
                        // Fully-approved entry already present — return it unchanged (idempotent re-approval)
                        _logger.Warning(
                            "Idempotent save: DsmEntry {Id} for {Dsm}/Pump {Pump} is already fully approved. Returning existing record.",
                            existingMatch.DsmEntryId, dsmName, pumpId);
                        var fullExisting = await context.Set<DsmEntry>()
                            .Include(e => e.NozzleReadings)
                            .Include(e => e.PaymentCollection)
                            .Include(e => e.DebitEntries)
                            .Include(e => e.TestingEntries)
                            .Include(e => e.Expenses)
                            .Include(e => e.CashDenominations)
                            .Include(e => e.PersonalDebtors)
                            .Include(e => e.KhandharePetroleumEntries)
                            .FirstOrDefaultAsync(e => e.DsmEntryId == existingMatch.DsmEntryId);
                        return Result<DsmEntry>.Ok(fullExisting ?? existingMatch);
                    }
                }
            }

            // Create or update DSM entry
            DsmEntry entry;
            if (existingEntryId.HasValue && existingEntryId.Value != 0)
            {
                var existing = await context.Set<DsmEntry>().FindAsync(existingEntryId.Value);
                if (existing != null)
                {
                    existing.ShiftId = shift.ShiftId;
                    existing.DsmName = dsmName;
                    existing.PumpId = pumpId;
                    existing.ConnectedPumpId = connectedPumpId;
                    existing.StartTime = startTime;
                    existing.EndTime = endTime;
                    existing.UpdatedAt = DateTime.Now;
                    entry = existing;
                }
                else
                {
                    entry = new DsmEntry
                    {
                        ShiftId = shift.ShiftId,
                        DsmName = dsmName,
                        PumpId = pumpId,
                        ConnectedPumpId = connectedPumpId,
                        StartTime = startTime,
                        EndTime = endTime,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };
                    context.Set<DsmEntry>().Add(entry);
                }
            }
            else
            {
                entry = new DsmEntry
                {
                    ShiftId = shift.ShiftId,
                    DsmName = dsmName,
                    PumpId = pumpId,
                    ConnectedPumpId = connectedPumpId,
                    StartTime = startTime,
                    EndTime = endTime,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                context.Set<DsmEntry>().Add(entry);
            }

            await context.SaveChangesAsync();
            var savedEntryId = entry.DsmEntryId;

            // Save nozzle readings
            var existingNozzles = await context.Set<NozzleReading>().Where(r => r.DsmEntryId == savedEntryId).ToListAsync();
            context.Set<NozzleReading>().RemoveRange(existingNozzles);
            foreach (var r in primaryReadings)
            {
                r.DsmEntryId = savedEntryId;
                r.DsmEntry = null;
                r.SaleLitres = r.ClosingReading - r.OpeningReading;
                r.Amount = r.SaleLitres * r.Rate;
            }
            context.Set<NozzleReading>().AddRange(primaryReadings);

            // Save Payment Collection
            var existingPayment = await context.Set<PaymentCollection>().FirstOrDefaultAsync(p => p.DsmEntryId == savedEntryId);
            if (existingPayment != null)
            {
                payment.PaymentId = existingPayment.PaymentId;
                payment.DsmEntryId = savedEntryId;
                payment.DsmEntry = null;
                context.Entry(existingPayment).CurrentValues.SetValues(payment);
            }
            else
            {
                payment.DsmEntryId = savedEntryId;
                payment.DsmEntry = null;
                context.Set<PaymentCollection>().Add(payment);
            }

            // Save Debits
            var existingDebits = await context.Set<DebitEntry>().Where(d => d.DsmEntryId == savedEntryId).ToListAsync();
            context.Set<DebitEntry>().RemoveRange(existingDebits);
            foreach (var d in debits)
            {
                d.DsmEntryId = savedEntryId;
                d.DsmEntry = null;
            }
            context.Set<DebitEntry>().AddRange(debits);

            // Save Testing
            var existingTesting = await context.Set<TestingEntry>().Where(t => t.DsmEntryId == savedEntryId).ToListAsync();
            context.Set<TestingEntry>().RemoveRange(existingTesting);
            foreach (var t in testingEntries)
            {
                t.DsmEntryId = savedEntryId;
                t.DsmEntry = null;
            }
            context.Set<TestingEntry>().AddRange(testingEntries);

            // Save Expenses
            var existingExpenses = await context.Set<Expense>().Where(e => e.DsmEntryId == savedEntryId).ToListAsync();
            context.Set<Expense>().RemoveRange(existingExpenses);
            foreach (var e in expenses)
            {
                e.DsmEntryId = savedEntryId;
                e.DsmEntry = null;
            }
            context.Set<Expense>().AddRange(expenses);

            // Save Cash Denominations
            var existingCash = await context.Set<CashDenomination>().Where(c => c.DsmEntryId == savedEntryId).ToListAsync();
            context.Set<CashDenomination>().RemoveRange(existingCash);
            foreach (var c in cashDenominations)
            {
                c.DsmEntryId = savedEntryId;
                c.DsmEntry = null;
            }
            context.Set<CashDenomination>().AddRange(cashDenominations);

            // Save Personal Debtors
            var existingPersonal = await context.Set<DsmPersonalDebtor>().Where(p => p.DsmEntryId == savedEntryId).ToListAsync();
            context.Set<DsmPersonalDebtor>().RemoveRange(existingPersonal);
            if (personalDebtors != null)
            {
                foreach (var p in personalDebtors)
                {
                    p.DsmEntryId = savedEntryId;
                    p.DsmEntry = null;
                    p.DsmName = dsmName;
                    p.Date = shift.ShiftDate;
                    if (string.IsNullOrEmpty(p.Time))
                    {
                        p.Time = DateTime.Now.ToString("hh:mm tt");
                    }
                }
                context.Set<DsmPersonalDebtor>().AddRange(personalDebtors);
            }

            // Save Khandhare Petroleum Drawings
            var existingKP = await context.Set<KhandharePetroleumEntry>().Where(kp => kp.DsmEntryId == savedEntryId).ToListAsync();
            context.Set<KhandharePetroleumEntry>().RemoveRange(existingKP);
            if (khandharePetroleumEntries != null)
            {
                foreach (var kp in khandharePetroleumEntries)
                {
                    kp.DsmEntryId = savedEntryId;
                    kp.DsmEntry = null;
                    kp.DsmName = dsmName;
                    kp.Date = shift.ShiftDate;
                }
                context.Set<KhandharePetroleumEntry>().AddRange(khandharePetroleumEntries);
            }

            await context.SaveChangesAsync();

            // Recalculate canonical totals for primary entry
            var fullEntry = await context.Set<DsmEntry>()
                .Include(e => e.NozzleReadings)
                .Include(e => e.PaymentCollection)
                .Include(e => e.DebitEntries)
                .Include(e => e.TestingEntries)
                .Include(e => e.Expenses)
                .Include(e => e.CashDenominations)
                .Include(e => e.PersonalDebtors)
                .Include(e => e.KhandharePetroleumEntries)
                .FirstOrDefaultAsync(e => e.DsmEntryId == savedEntryId);

            if (fullEntry != null)
            {
                var calc = _dsmCalculationService.Calculate(ToCalculationDto(fullEntry));
                var connectedGross = connectedPumpId.HasValue ? (decimal)connectedReadings.Sum(x => x.Amount) : 0m;
                entry.GrossSales = calc.GrossSales + connectedGross;
                entry.TotalInDirect = calc.TotalInDirect;
                entry.TotalCreditors = calc.TotalCreditors;
                entry.TotalCollection = calc.TotalCollection;
                entry.Mismatch = calc.TotalCollection - entry.GrossSales;
                entry.UpdatedAt = DateTime.Now;
                context.Entry(entry).State = EntityState.Modified;
                await context.SaveChangesAsync();
            }

            // Save connected pump entry if specified
            if (connectedPumpId.HasValue)
            {
                var shiftEntries = await context.Set<DsmEntry>()
                    .Where(e => e.ShiftId == shift.ShiftId)
                    .ToListAsync();
                
                var existingConnectedEntry = shiftEntries.FirstOrDefault(e =>
                    e.PumpId == connectedPumpId.Value &&
                    string.Equals(e.DsmName, dsmName, StringComparison.OrdinalIgnoreCase));

                DsmEntry connectedEntry;
                if (existingConnectedEntry != null)
                {
                    existingConnectedEntry.ReconciledToPumpId = pumpId;
                    existingConnectedEntry.StartTime = startTime;
                    existingConnectedEntry.EndTime = endTime;
                    existingConnectedEntry.UpdatedAt = DateTime.Now;
                    connectedEntry = existingConnectedEntry;
                }
                else
                {
                    connectedEntry = new DsmEntry
                    {
                        ShiftId = shift.ShiftId,
                        DsmName = dsmName,
                        PumpId = connectedPumpId.Value,
                        ReconciledToPumpId = pumpId,
                        StartTime = startTime,
                        EndTime = endTime,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };
                    context.Set<DsmEntry>().Add(connectedEntry);
                }

                await context.SaveChangesAsync();
                var savedConnEntryId = connectedEntry.DsmEntryId;

                // Save nozzle readings for connected pump
                var existingConnNozzles = await context.Set<NozzleReading>().Where(r => r.DsmEntryId == savedConnEntryId).ToListAsync();
                context.Set<NozzleReading>().RemoveRange(existingConnNozzles);
                foreach (var r in connectedReadings)
                {
                    r.DsmEntryId = savedConnEntryId;
                    r.DsmEntry = null;
                    r.SaleLitres = r.ClosingReading - r.OpeningReading;
                    r.Amount = r.SaleLitres * r.Rate;
                }
                context.Set<NozzleReading>().AddRange(connectedReadings);

                // Save empty child tables for connected pump to avoid null refs
                var existingConnPayment = await context.Set<PaymentCollection>().FirstOrDefaultAsync(p => p.DsmEntryId == savedConnEntryId);
                if (existingConnPayment == null)
                {
                    context.Set<PaymentCollection>().Add(new PaymentCollection { DsmEntryId = savedConnEntryId });
                }
                
                var existingConnDebits = await context.Set<DebitEntry>().Where(d => d.DsmEntryId == savedConnEntryId).ToListAsync();
                context.Set<DebitEntry>().RemoveRange(existingConnDebits);
                var existingConnTesting = await context.Set<TestingEntry>().Where(t => t.DsmEntryId == savedConnEntryId).ToListAsync();
                context.Set<TestingEntry>().RemoveRange(existingConnTesting);
                var existingConnExpenses = await context.Set<Expense>().Where(e => e.DsmEntryId == savedConnEntryId).ToListAsync();
                context.Set<Expense>().RemoveRange(existingConnExpenses);
                var existingConnCash = await context.Set<CashDenomination>().Where(c => c.DsmEntryId == savedConnEntryId).ToListAsync();
                context.Set<CashDenomination>().RemoveRange(existingConnCash);

                await context.SaveChangesAsync();

                // Recalculate totals for connected entry
                var fullConnEntry = await context.Set<DsmEntry>()
                    .Include(e => e.NozzleReadings)
                    .Include(e => e.PaymentCollection)
                    .Include(e => e.DebitEntries)
                    .Include(e => e.TestingEntries)
                    .Include(e => e.Expenses)
                    .Include(e => e.CashDenominations)
                    .Include(e => e.PersonalDebtors)
                    .Include(e => e.KhandharePetroleumEntries)
                    .FirstOrDefaultAsync(e => e.DsmEntryId == savedConnEntryId);

                if (fullConnEntry != null)
                {
                    var calc = _dsmCalculationService.Calculate(ToCalculationDto(fullConnEntry));
                    connectedEntry.GrossSales = calc.GrossSales;
                    connectedEntry.TotalInDirect = calc.TotalInDirect;
                    connectedEntry.TotalCreditors = calc.TotalCreditors;
                    connectedEntry.TotalCollection = calc.TotalCollection;
                    connectedEntry.Mismatch = 0m;
                    connectedEntry.UpdatedAt = DateTime.Now;
                    context.Entry(connectedEntry).State = EntityState.Modified;
                    await context.SaveChangesAsync();
                }
            }
            else
            {
                // Clear any existing ReconciledToPumpId links for this primary pump in the shift
                var candidates = await context.Set<DsmEntry>()
                    .Where(e => e.ShiftId == shift.ShiftId && e.ReconciledToPumpId == pumpId && (e.DsmName != null && dsmName != null && e.DsmName.ToLower() == dsmName.ToLower()))
                    .ToListAsync();
                foreach (var candidate in candidates)
                {
                    candidate.ReconciledToPumpId = null;
                    candidate.UpdatedAt = DateTime.Now;
                }
                await context.SaveChangesAsync();
            }

            // Sync pairing properties for other entries if relevant
            var shiftEntriesList = await context.Set<DsmEntry>()
                .Where(e => e.ShiftId == shift.ShiftId)
                .ToListAsync();
            
            var firstEntry = shiftEntriesList.FirstOrDefault(e => e.DsmEntryId == savedEntryId);
            if (firstEntry != null)
            {
                firstEntry.ConnectedPumpId = connectedPumpId;
                context.Entry(firstEntry).State = EntityState.Modified;
            }

            foreach (var candidate in shiftEntriesList.Where(e =>
                         e.DsmEntryId != savedEntryId
                         && string.Equals(e.DsmName, dsmName, StringComparison.OrdinalIgnoreCase)))
            {
                if (connectedPumpId.HasValue && candidate.PumpId == connectedPumpId.Value)
                {
                    candidate.ReconciledToPumpId = pumpId;
                    context.Entry(candidate).State = EntityState.Modified;
                }

                if (candidate.ConnectedPumpId == pumpId)
                {
                    var current = shiftEntriesList.FirstOrDefault(e => e.DsmEntryId == savedEntryId);
                    if (current != null)
                    {
                        current.ReconciledToPumpId = candidate.PumpId;
                        context.Entry(current).State = EntityState.Modified;
                    }
                }
            }
            await context.SaveChangesAsync();

            // Propagate nozzle readings downstream
            await PropagateNozzleReadingsWithContextAsync(context, date, shiftType);

            _logger.Information("DSM entry saved successfully via transaction context: {DsmName} Pump {PumpId}", dsmName, pumpId);
            RaiseDsmEntryChanged();
            return Result<DsmEntry>.Ok(entry);
        }
        catch (DbUpdateException)
        {
            throw; // Let the caller catch and log with complete details!
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save complete DSM entry with context");
            return Result<DsmEntry>.Fail($"Failed to save DSM entry: {ex.Message}");
        }
    }

    public async Task<Result> PropagateNozzleReadingsWithContextAsync(DbContext context, DateTime startDate, string startShiftType)
    {
        try
        {
            // Load shifts from startDate onwards (up to 30 days forward to keep it bounded)
            var shifts = await context.Set<Shift>()
                .Where(s => s.ShiftDate >= startDate.Date && s.ShiftDate <= startDate.Date.AddDays(30))
                .ToListAsync();

            // Sort operationally: Date ascending, then Shift B (Day) before Shift A (Night)
            var sortedShifts = shifts
                .OrderBy(s => s.ShiftDate)
                .ThenBy(s => s.ShiftType == "A")
                .ToList();

            var startIdx = sortedShifts.FindIndex(s => s.ShiftDate.Date == startDate.Date && string.Equals(s.ShiftType, startShiftType, StringComparison.OrdinalIgnoreCase));
            if (startIdx < 0) return Result.Ok();

            // Build a map of previous nozzle closings from the shift before startIdx
            var prevClosings = new Dictionary<int, double>();
            if (startIdx > 0)
            {
                var prevShift = sortedShifts[startIdx - 1];
                var prevEntries = await context.Set<DsmEntry>()
                    .AsNoTracking()
                    .Include(e => e.NozzleReadings)
                    .Where(e => e.ShiftId == prevShift.ShiftId)
                    .ToListAsync();
                
                foreach (var entry in prevEntries)
                {
                    foreach (var r in entry.NozzleReadings)
                    {
                        prevClosings[r.NozzleNumber] = r.ClosingReading;
                    }
                }
            }

            for (int i = startIdx; i < sortedShifts.Count; i++)
            {
                var currentShift = sortedShifts[i];
                var entries = await context.Set<DsmEntry>()
                    .Include(e => e.NozzleReadings)
                    .Include(e => e.PaymentCollection)
                    .Include(e => e.DebitEntries)
                    .Include(e => e.TestingEntries)
                    .Include(e => e.Expenses)
                    .Include(e => e.CashDenominations)
                    .Include(e => e.PersonalDebtors)
                    .Include(e => e.KhandharePetroleumEntries)
                    .Where(e => e.ShiftId == currentShift.ShiftId)
                    .ToListAsync();

                foreach (var entry in entries)
                {
                    bool entryChanged = false;
                    var updatedReadings = new List<NozzleReading>();

                    foreach (var reading in entry.NozzleReadings)
                    {
                        double newOpening = reading.OpeningReading;

                        if (prevClosings.TryGetValue(reading.NozzleNumber, out var prevClosing))
                        {
                            newOpening = prevClosing;
                            if (newOpening > reading.ClosingReading)
                            {
                                newOpening = reading.ClosingReading;
                                _logger.Warning("Nozzle {NozzleNumber} prev closing {PrevClosing} > current closing {CurrentClosing} in {Date} {Shift}. Capping.",
                                    reading.NozzleNumber, prevClosing, reading.ClosingReading, currentShift.ShiftDate, currentShift.ShiftType);
                            }
                        }

                        if (Math.Abs(reading.OpeningReading - newOpening) > 0.001)
                        {
                            reading.OpeningReading = newOpening;
                            reading.SaleLitres = reading.ClosingReading - reading.OpeningReading;
                            reading.Amount = reading.SaleLitres * reading.Rate;
                            entryChanged = true;
                        }

                        updatedReadings.Add(reading);
                    }

                    if (entryChanged)
                    {
                        // Save nozzle readings directly to context
                        var existingReadings = await context.Set<NozzleReading>().Where(r => r.DsmEntryId == entry.DsmEntryId).ToListAsync();
                        context.Set<NozzleReading>().RemoveRange(existingReadings);
                        foreach (var ur in updatedReadings)
                        {
                            ur.DsmEntryId = entry.DsmEntryId;
                            ur.DsmEntry = null;
                        }
                        context.Set<NozzleReading>().AddRange(updatedReadings);
                        await context.SaveChangesAsync();

                        // Recalculate totals
                        var calc = _dsmCalculationService.Calculate(ToCalculationDto(entry));
                        entry.GrossSales = calc.GrossSales;
                        entry.TotalInDirect = calc.TotalInDirect;
                        entry.TotalCreditors = calc.TotalCreditors;
                        entry.TotalCollection = calc.TotalCollection;
                        entry.Mismatch = calc.TotalCollection - entry.GrossSales;
                        entry.UpdatedAt = DateTime.Now;
                        context.Entry(entry).State = EntityState.Modified;
                        await context.SaveChangesAsync();
                    }

                    // Update prevClosings map with this entry's nozzle closings
                    foreach (var r in entry.NozzleReadings)
                    {
                        prevClosings[r.NozzleNumber] = r.ClosingReading;
                    }
                }
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to propagate nozzle readings starting from {Date} {Shift} using context", startDate, startShiftType);
            return Result.Fail($"Failed to propagate nozzle readings: {ex.Message}");
        }
    }
}


