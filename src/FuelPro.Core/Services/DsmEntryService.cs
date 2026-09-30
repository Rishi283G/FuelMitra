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

    public static event Action? DsmProfileChanged;
    public static void RaiseDsmProfileChanged() => DsmProfileChanged?.Invoke();

    public static event Action? PayrollChanged;
    public static void RaisePayrollChanged() => PayrollChanged?.Invoke();

    public static event Action? SettingsChanged;
    public static void RaiseSettingsChanged() => SettingsChanged?.Invoke();

    public static event Action? StationConfigurationChanged;
    public static void RaiseStationConfigurationChanged() => StationConfigurationChanged?.Invoke();

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
        List<KhandharePetroleumEntry>? khandharePetroleumEntries = null,
        List<DsmQrPaymentEntry>? qrPayments = null,
        IEnumerable<int>? connectedPumpIds = null)
    {
        try
        {
            // 1. Resolve effective connected pumps with backward-compatible fallback
            List<int> effectiveConnectedPumps;
            var resolvedFromInput = PumpConnectionConfiguration.ResolveEffectiveConnectedPumpIds(connectedPumpId, null, connectedPumpIds)
                .Where(id => id != pumpId)
                .ToList();

            if (resolvedFromInput.Count > 0 || connectedPumpIds != null || (connectedPumpId.HasValue && connectedPumpId.Value > 0))
            {
                effectiveConnectedPumps = resolvedFromInput;
            }
            else
            {
                var stationConfigService = _serviceProvider.GetService<IStationConfigurationService>();
                if (stationConfigService != null)
                {
                    var config = await stationConfigService.GetPumpConnectionConfigurationAsync();
                    if (config != null && config.IsEnabled)
                    {
                        var grp = config.Groups.FirstOrDefault(g => g.PrimaryPumpId == pumpId);
                        effectiveConnectedPumps = grp != null
                            ? grp.ConnectedPumpIds.Where(id => id > 0 && id != pumpId).Distinct().ToList()
                            : new List<int>();
                    }
                    else
                    {
                        effectiveConnectedPumps = new List<int>();
                    }
                }
                else
                {
                    effectiveConnectedPumps = new List<int>();
                }
            }

            var allEffectivePumps = new List<int> { pumpId }.Concat(effectiveConnectedPumps).Distinct().ToList();

            // 2. Strict Upfront Validation (BEFORE ANY DB WRITE)
            // Duplicate nozzle reading check
            var duplicateNozzle = nozzleReadings
                .GroupBy(r => r.NozzleNumber)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicateNozzle != null)
            {
                return Result<DsmEntry>.Fail($"Duplicate nozzle reading found for Nozzle {duplicateNozzle.Key}.");
            }

            // Nozzle ownership validation - must belong to configured group and resolve to a valid physical pump
            foreach (var nr in nozzleReadings)
            {
                var nozzlePumpId = PumpConfiguration.GetPumpIdForNozzle(nr.NozzleNumber, date);
                if (nozzlePumpId <= 0)
                {
                    return Result<DsmEntry>.Fail($"Nozzle {nr.NozzleNumber} could not be resolved to a valid pump for date {date:yyyy-MM-dd}.");
                }
                if (!allEffectivePumps.Contains(nozzlePumpId))
                {
                    return Result<DsmEntry>.Fail($"Nozzle {nr.NozzleNumber} belongs to Pump {nozzlePumpId}, which is not part of connection group [{string.Join(", ", allEffectivePumps)}].");
                }
            }

            // Check shift lock
            var shiftResult = await _shiftRepo.GetOrCreateShiftAsync(date, shiftType);
            if (!shiftResult.Success) return Result<DsmEntry>.Fail(shiftResult.Error);
            var shift = shiftResult.Data!;

            if (shift.IsLocked)
                return Result<DsmEntry>.Fail("This shift is locked and cannot be edited.");

            // Pre-calculate nozzle readings SaleLitres and Amount in memory so they are available for gross sales calculations
            foreach (var nr in nozzleReadings)
            {
                nr.SaleLitres = nr.ClosingReading - nr.OpeningReading;
                nr.Amount = nr.SaleLitres * nr.Rate;
            }

            // Dynamic partitioning of nozzle readings by physical pump
            var readingsByPump = nozzleReadings
                .GroupBy(nr => PumpConfiguration.GetPumpIdForNozzle(nr.NozzleNumber, date))
                .ToDictionary(g => g.Key, g => g.ToList());

            var primaryReadings = readingsByPump.TryGetValue(pumpId, out var pr) ? pr : new List<NozzleReading>();

            // Create or update primary DSM entry
            var entry = new DsmEntry
            {
                DsmEntryId = existingEntryId ?? 0,
                ShiftId = shift.ShiftId,
                DsmName = dsmName,
                PumpId = pumpId,
                ConnectedPumpId = effectiveConnectedPumps.FirstOrDefault() > 0 ? effectiveConnectedPumps.FirstOrDefault() : null,
                ConnectedPumpIdsJson = effectiveConnectedPumps.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(effectiveConnectedPumps) : null,
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

                // Save Cross-DSM QR Payments
                var existingQr = await dbContext.Set<DsmQrPaymentEntry>().Where(q => q.DsmEntryId == savedEntry.DsmEntryId).ToListAsync();
                dbContext.Set<DsmQrPaymentEntry>().RemoveRange(existingQr);

                if (qrPayments != null && qrPayments.Count > 0)
                {
                    foreach (var q in qrPayments)
                    {
                        q.DsmEntryId = savedEntry.DsmEntryId;
                        q.DsmEntry = null;
                        q.DsmName = dsmName;
                        q.Date = shift.ShiftDate;
                    }
                    dbContext.Set<DsmQrPaymentEntry>().AddRange(qrPayments);
                }

                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save child collections for DsmEntryId {DsmEntryId}", savedEntry.DsmEntryId);
            }

            // Re-load full entry and persist canonical totals.
            var fullResult = await _dsmRepo.GetFullEntryAsync(savedEntry.DsmEntryId);
            if (fullResult.Success && fullResult.Data != null)
            {
                var calc = _dsmCalculationService.Calculate(ToCalculationDto(fullResult.Data));
                var totalConnectedGross = (decimal)effectiveConnectedPumps
                    .Sum(slavePumpId => readingsByPump.TryGetValue(slavePumpId, out var sr) ? sr.Sum(x => x.Amount) : 0);

                var existingSlavesRes = await _dsmRepo.GetEntriesForShiftAsync(shift.ShiftId);
                var existingSlaves = existingSlavesRes.Success && existingSlavesRes.Data != null ? existingSlavesRes.Data : new List<DsmEntry>();
                var slaveTesting = (decimal)effectiveConnectedPumps.Sum(spId =>
                    existingSlaves.Where(e => e.PumpId == spId && e.TestingEntries != null)
                                  .SelectMany(e => e.TestingEntries)
                                  .Sum(t => t.Amount));
                var groupTesting = (fullResult.Data.TestingEntries?.Sum(t => (decimal)t.Amount) ?? 0m) + slaveTesting;

                savedEntry.GrossSales = calc.GrossSales + totalConnectedGross;
                savedEntry.TotalInDirect = calc.TotalInDirect;
                savedEntry.TotalCreditors = calc.TotalCreditors;
                savedEntry.TotalCollection = calc.TotalCollection;
                var netGrossSales = savedEntry.GrossSales - groupTesting;
                savedEntry.Mismatch = calc.TotalCollection - netGrossSales;
                await _dsmRepo.SaveEntryAsync(savedEntry);

                // Automatic DSM Loss (Personal Debtor) handling based on shortage (no tolerance)
                try
                {
                    double totalShortage = savedEntry.Mismatch < 0 ? (double)Math.Abs(savedEntry.Mismatch) : 0;
                    double dsmLossAmount = totalShortage;
                    var currentPDsRes = await _personalDebtorRepo.GetByDsmEntryIdAsync(savedEntry.DsmEntryId);
                    var pdList = currentPDsRes.Success && currentPDsRes.Data != null ? currentPDsRes.Data : new List<DsmPersonalDebtor>();
                    bool pdChanged = false;

                    var existingShortage = pdList.FirstOrDefault(p => p.Remarks != null && p.Remarks.Contains("Shortage"));
                    if (dsmLossAmount > 0)
                    {
                        if (existingShortage != null)
                        {
                            if (Math.Abs(existingShortage.Amount - dsmLossAmount) > 0.01)
                            {
                                existingShortage.Amount = dsmLossAmount;
                                pdChanged = true;
                            }
                        }
                        else
                        {
                            pdList.Add(new DsmPersonalDebtor
                            {
                                DsmEntryId = savedEntry.DsmEntryId,
                                DsmName = dsmName,
                                Date = shift.ShiftDate,
                                Time = DateTime.Now.ToString("hh:mm tt"),
                                Amount = dsmLossAmount,
                                Remarks = $"Auto Shift Shortage (Pump {pumpId}, Shift {shiftType})",
                                PaymentMethod = "Cash"
                            });
                            pdChanged = true;
                        }
                    }
                    else if (existingShortage != null)
                    {
                        pdList.Remove(existingShortage);
                        pdChanged = true;
                    }

                    if (pdChanged)
                    {
                        await _personalDebtorRepo.SavePersonalDebtorsAsync(savedEntry.DsmEntryId, pdList);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to auto-update DSM Personal Debtor shortage for DsmEntryId {DsmEntryId}", savedEntry.DsmEntryId);
                }
            }

            // Save slave DsmEntry for each connected pump
            var shiftEntriesResult = await _dsmRepo.GetEntriesForShiftAsync(shift.ShiftId);
            var shiftEntries = shiftEntriesResult.Success && shiftEntriesResult.Data != null ? shiftEntriesResult.Data : new List<DsmEntry>();
            var activeSlaveEntryIds = new HashSet<int>();

            foreach (var slavePumpId in effectiveConnectedPumps)
            {
                var existingSlaveEntry = shiftEntries.FirstOrDefault(e =>
                    savedEntry.DsmEntryId != 0 && e.ReconciledToPumpId == savedEntry.DsmEntryId && e.PumpId == slavePumpId);

                var connectedEntry = new DsmEntry
                {
                    DsmEntryId = existingSlaveEntry?.DsmEntryId ?? 0,
                    ShiftId = shift.ShiftId,
                    DsmName = dsmName,
                    PumpId = slavePumpId,
                    ReconciledToPumpId = savedEntry.DsmEntryId,
                    StartTime = startTime,
                    EndTime = endTime
                };

                var saveConnResult = await _dsmRepo.SaveEntryAsync(connectedEntry);
                if (saveConnResult.Success)
                {
                    var savedConnectedEntry = saveConnResult.Data!;
                    activeSlaveEntryIds.Add(savedConnectedEntry.DsmEntryId);

                    var slaveReadings = readingsByPump.TryGetValue(slavePumpId, out var sr) ? sr : new List<NozzleReading>();
                    await _nozzleRepo.SaveReadingsAsync(savedConnectedEntry.DsmEntryId, slaveReadings);

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
                        savedConnectedEntry.TotalInDirect = 0m;
                        savedConnectedEntry.TotalCreditors = 0m;
                        savedConnectedEntry.TotalCollection = 0m;
                        savedConnectedEntry.Mismatch = 0m; // Connected entry mismatch is always 0 because collections are in primary
                        await _dsmRepo.SaveEntryAsync(savedConnectedEntry);
                    }
                }
            }

            // Remove orphaned slave entries (e.g. if edited connection group had more slaves previously)
            var orphanedSlaves = shiftEntries.Where(e =>
                savedEntry.DsmEntryId != 0
                && e.ReconciledToPumpId == savedEntry.DsmEntryId
                && !activeSlaveEntryIds.Contains(e.DsmEntryId)).ToList();

            if (orphanedSlaves.Count > 0)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
                    foreach (var orphan in orphanedSlaves)
                    {
                        var entryToDelete = await dbContext.Set<DsmEntry>().FindAsync(orphan.DsmEntryId);
                        if (entryToDelete != null)
                        {
                            dbContext.Set<DsmEntry>().Remove(entryToDelete);
                        }
                    }
                    await dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to clean up orphaned slave entries for DsmEntryId {DsmEntryId}", savedEntry.DsmEntryId);
                }
            }

            // Sync ConnectedPumpId and ConnectedPumpIdsJson for saved primary entry
            var finalShiftEntriesResult = await _dsmRepo.GetEntriesForShiftAsync(shift.ShiftId);
            if (finalShiftEntriesResult.Success && finalShiftEntriesResult.Data != null)
            {
                var finalEntries = finalShiftEntriesResult.Data;
                var firstEntry = finalEntries.FirstOrDefault(e => e.DsmEntryId == savedEntry.DsmEntryId);
                if (firstEntry != null)
                {
                    var targetConnId = effectiveConnectedPumps.FirstOrDefault() > 0 ? effectiveConnectedPumps.FirstOrDefault() : (int?)null;
                    var targetConnJson = effectiveConnectedPumps.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(effectiveConnectedPumps) : null;
                    if (firstEntry.ConnectedPumpId != targetConnId || firstEntry.ConnectedPumpIdsJson != targetConnJson)
                    {
                        firstEntry.ConnectedPumpId = targetConnId;
                        firstEntry.ConnectedPumpIdsJson = targetConnJson;
                        await _dsmRepo.SaveEntryAsync(firstEntry);
                    }
                }
            }

            _logger.Information("DSM entry saved successfully: {DsmName} Pump {PumpId} with {SlaveCount} slaves", dsmName, pumpId, effectiveConnectedPumps.Count);
            RaiseDsmEntryChanged();

            var finalReload = await _dsmRepo.GetFullEntryAsync(savedEntry.DsmEntryId);
            var returnEntry = finalReload.Success && finalReload.Data != null ? finalReload.Data : savedEntry;
            if (returnEntry.TestingEntries == null)
            {
                returnEntry.TestingEntries = testingEntries;
            }
            return Result<DsmEntry>.Ok(returnEntry);
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
    /// Gets full dynamic fuel rates dictionary from settings.
    /// </summary>
    public async Task<Dictionary<string, double>> GetFuelRatesMapAsync()
    {
        var result = await _settingsRepo.GetSettingsAsync();
        if (result.Success && result.Data != null)
            return result.Data.FuelRates;
        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["HSD - 20KL"] = 90.35,
            ["MS - 20KL"] = 103.81,
            ["HSD - 20KL II"] = 90.35,
            ["CNG"] = 85.0
        };
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

            var allEntries = entriesResult.Data!;
            var primaryEntries = allEntries
                .Where(e => !e.ReconciledToPumpId.HasValue)
                .OrderBy(e => e.DsmEntryId)
                .ToList();
            var allSlaves = allEntries.Where(e => e.ReconciledToPumpId.HasValue).ToList();
            var usedSlaveIds = new HashSet<int>();
            var summaries = new List<DsmEntrySummaryDto>();

            int sequenceCounter = 1;
            foreach (var primary in primaryEntries)
            {
                var primaryConnectedPumps = primary.GetEffectiveConnectedPumpIds();
                var connectedSlaves = allSlaves
                    .Where(e => !usedSlaveIds.Contains(e.DsmEntryId)
                        && (e.ReconciledToPumpId == primary.DsmEntryId 
                            || (primaryConnectedPumps.Contains(e.PumpId) && string.Equals((e.DsmName ?? "").Trim(), (primary.DsmName ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                            || (primary.ConnectedPumpId.HasValue && e.PumpId == primary.ConnectedPumpId.Value && string.Equals((e.DsmName ?? "").Trim(), (primary.DsmName ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                            || (e.ReconciledToPumpId == primary.PumpId && string.Equals((e.DsmName ?? "").Trim(), (primary.DsmName ?? "").Trim(), StringComparison.OrdinalIgnoreCase))))
                    .ToList();

                foreach (var s in connectedSlaves)
                {
                    usedSlaveIds.Add(s.DsmEntryId);
                }

                var group = new List<DsmEntry> { primary };
                group.AddRange(connectedSlaves);

                var distinctNozzles = group
                    .SelectMany(e => e.NozzleReadings ?? new List<NozzleReading>())
                    .GroupBy(n => n.NozzleNumber)
                    .Select(g => g.First())
                    .ToList();

                double totalGrossSales = distinctNozzles.Count > 0 
                    ? distinctNozzles.Sum(n => (double)n.Amount)
                    : (primary.GrossSales > 0 ? (double)primary.GrossSales : (double)group.Sum(e => e.GrossSales));

                var calcDto = ToCalculationDto(primary);
                var calc = _dsmCalculationService.Calculate(calcDto);
                double totalCollection = (double)calc.TotalCollection;
                if (totalCollection == 0 && primary.TotalCollection > 0)
                {
                    totalCollection = (double)primary.TotalCollection;
                }

                double totalTesting = (double)group
                    .SelectMany(e => e.TestingEntries ?? new List<TestingEntry>())
                    .Sum(t => t.Amount);

                double netSales = totalGrossSales - totalTesting;
                double mismatch = primary.Mismatch != 0 ? (double)primary.Mismatch : (totalCollection - netSales);

                summaries.Add(new DsmEntrySummaryDto
                {
                    SequenceNo = sequenceCounter++,
                    DsmEntryId = primary.DsmEntryId,
                    DsmName = primary.DsmName,
                    PumpId = primary.PumpId,
                    ConnectedPumpId = primary.ConnectedPumpId ?? connectedSlaves.FirstOrDefault()?.PumpId,
                    ConnectedPumpIdsJson = primary.ConnectedPumpIdsJson ?? (connectedSlaves.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(connectedSlaves.Select(s => s.PumpId).Distinct()) : null),
                    ReconciledToPumpId = null,
                    GrossSales = totalGrossSales,
                    TestingAmount = totalTesting,
                    NetSales = netSales,
                    TotalPaymentIn = totalCollection,
                    Difference = mismatch,
                    CreatedAt = primary.CreatedAt
                });
            }


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
                                       + (entry.PaymentCollection?.PhonePeCardNight ?? 0)
                                       + (entry.QrPayments?.Sum(q => q.Amount) ?? 0)),
                CreditCard   = (decimal)((entry.PaymentCollection?.CreditCardMorning ?? 0)
                                       + (entry.PaymentCollection?.CreditCardDay ?? 0)
                                       + (entry.PaymentCollection?.CreditCardNight ?? 0)),
                PetroCard    = (decimal)((entry.PaymentCollection?.PetroCardMorning ?? 0)
                                       + (entry.PaymentCollection?.PetroCardDay ?? 0)
                                       + (entry.PaymentCollection?.PetroCardNight ?? 0)),
                CashDeposit  = (decimal)(cash1Total > 0 ? cash1Total : (entry.PaymentCollection?.CashDeposit ?? 0)),
                PhysicalCash = (decimal)cash2Total,
                DynamicPayments = (decimal)(entry.PaymentCollection?.Items?.Sum(i => i.Amount) ?? 0),
                Others       = (decimal)(entry.PaymentCollection?.Others ?? 0)
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
        // Saved entries are finalized upon submission and must not be retroactively mutated.
        await Task.CompletedTask;
        return Result.Ok();
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
        List<KhandharePetroleumEntry>? khandharePetroleumEntries = null,
        List<DsmQrPaymentEntry>? qrPayments = null,
        IEnumerable<int>? connectedPumpIds = null)
    {
        try
        {
            // 1. Resolve effective connected pumps with backward-compatible fallback
            List<int> effectiveConnectedPumps;
            var resolvedFromInput = PumpConnectionConfiguration.ResolveEffectiveConnectedPumpIds(connectedPumpId, null, connectedPumpIds)
                .Where(id => id != pumpId)
                .ToList();

            if (resolvedFromInput.Count > 0 || connectedPumpIds != null || (connectedPumpId.HasValue && connectedPumpId.Value > 0))
            {
                effectiveConnectedPumps = resolvedFromInput;
            }
            else
            {
                var stationConfigService = _serviceProvider.GetService<IStationConfigurationService>();
                if (stationConfigService != null)
                {
                    var config = await stationConfigService.GetPumpConnectionConfigurationAsync();
                    if (config != null && config.IsEnabled)
                    {
                        var grp = config.Groups.FirstOrDefault(g => g.PrimaryPumpId == pumpId);
                        effectiveConnectedPumps = grp != null
                            ? grp.ConnectedPumpIds.Where(id => id > 0 && id != pumpId).Distinct().ToList()
                            : new List<int>();
                    }
                    else
                    {
                        effectiveConnectedPumps = new List<int>();
                    }
                }
                else
                {
                    effectiveConnectedPumps = new List<int>();
                }
            }

            var allEffectivePumps = new List<int> { pumpId }.Concat(effectiveConnectedPumps).Distinct().ToList();

            // 2. Strict Upfront Validation (BEFORE ANY DB WRITE)
            var duplicateNozzle = nozzleReadings
                .GroupBy(r => r.NozzleNumber)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicateNozzle != null)
            {
                return Result<DsmEntry>.Fail($"Duplicate nozzle reading found for Nozzle {duplicateNozzle.Key}.");
            }

            foreach (var nr in nozzleReadings)
            {
                var nozzlePumpId = PumpConfiguration.GetPumpIdForNozzle(nr.NozzleNumber, date);
                if (nozzlePumpId <= 0)
                {
                    return Result<DsmEntry>.Fail($"Nozzle {nr.NozzleNumber} could not be resolved to a valid pump for date {date:yyyy-MM-dd}.");
                }
                if (!allEffectivePumps.Contains(nozzlePumpId))
                {
                    return Result<DsmEntry>.Fail($"Nozzle {nr.NozzleNumber} belongs to Pump {nozzlePumpId}, which is not part of connection group [{string.Join(", ", allEffectivePumps)}].");
                }
            }

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
                try
                {
                    await context.SaveChangesAsync();
                }
                catch (Exception)
                {
                    shift = await context.Set<Shift>()
                        .FirstOrDefaultAsync(s => s.ShiftDate == dateOnly && (s.ShiftType == shiftType || s.ShiftType == altShift));
                    if (shift == null)
                        throw;
                }
            }

            if (shift.IsLocked)
                return Result<DsmEntry>.Fail("This shift is locked and cannot be edited.");

            // Pre-calculate nozzle readings SaleLitres and Amount
            foreach (var nr in nozzleReadings)
            {
                nr.SaleLitres = nr.ClosingReading - nr.OpeningReading;
                nr.Amount = nr.SaleLitres * nr.Rate;
            }

            // Dynamic partitioning of nozzle readings by physical pump
            var readingsByPump = nozzleReadings
                .GroupBy(nr => PumpConfiguration.GetPumpIdForNozzle(nr.NozzleNumber, date))
                .ToDictionary(g => g.Key, g => g.ToList());

            var primaryReadings = readingsByPump.TryGetValue(pumpId, out var pr) ? pr : new List<NozzleReading>();

            // Create or update primary DSM entry
            DsmEntry? entry = null;
            if (existingEntryId.HasValue && existingEntryId.Value != 0)
            {
                entry = await context.Set<DsmEntry>().FindAsync(existingEntryId.Value);
            }

            if (entry != null)
            {
                entry.ShiftId = shift.ShiftId;
                entry.DsmName = dsmName;
                entry.PumpId = pumpId;
                entry.ConnectedPumpId = effectiveConnectedPumps.FirstOrDefault() > 0 ? effectiveConnectedPumps.FirstOrDefault() : null;
                entry.ConnectedPumpIdsJson = effectiveConnectedPumps.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(effectiveConnectedPumps) : null;
                entry.StartTime = startTime;
                entry.EndTime = endTime;
                entry.UpdatedAt = DateTime.Now;
            }
            else
            {
                entry = new DsmEntry
                {
                    ShiftId = shift.ShiftId,
                    DsmName = dsmName,
                    PumpId = pumpId,
                    ConnectedPumpId = effectiveConnectedPumps.FirstOrDefault() > 0 ? effectiveConnectedPumps.FirstOrDefault() : null,
                    ConnectedPumpIdsJson = effectiveConnectedPumps.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(effectiveConnectedPumps) : null,
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
            }
            context.Set<NozzleReading>().AddRange(primaryReadings);

            // Save Payment Collection
            var existingPayment = await context.Set<PaymentCollection>().FirstOrDefaultAsync(p => p.DsmEntryId == savedEntryId);
            if (existingPayment != null)
            {
                existingPayment.PhonePeMorning = payment.PhonePeMorning;
                existingPayment.PhonePeNight = payment.PhonePeNight;
                existingPayment.PhonePeDay = payment.PhonePeDay;
                existingPayment.PhonePeCardMorning = payment.PhonePeCardMorning;
                existingPayment.PhonePeCardNight = payment.PhonePeCardNight;
                existingPayment.PhonePeCardDay = payment.PhonePeCardDay;
                existingPayment.CreditCardMorning = payment.CreditCardMorning;
                existingPayment.CreditCardNight = payment.CreditCardNight;
                existingPayment.CreditCardDay = payment.CreditCardDay;
                existingPayment.PetroCardMorning = payment.PetroCardMorning;
                existingPayment.PetroCardNight = payment.PetroCardNight;
                existingPayment.PetroCardDay = payment.PetroCardDay;
                existingPayment.CashDeposit = payment.CashDeposit;
                existingPayment.Others = payment.Others;

                existingPayment.CardTid = payment.CardTid;
                existingPayment.CardBatch = payment.CardBatch;
                existingPayment.PhonePeTid = payment.PhonePeTid;
                existingPayment.PhonePeBatch = payment.PhonePeBatch;
                existingPayment.PetroCardTid = payment.PetroCardTid;
                existingPayment.PetroCardBatch = payment.PetroCardBatch;
                existingPayment.PhonePeTidMorning = payment.PhonePeTidMorning;
                existingPayment.PhonePeBatchMorning = payment.PhonePeBatchMorning;
                existingPayment.PhonePeTidNight = payment.PhonePeTidNight;
                existingPayment.PhonePeBatchNight = payment.PhonePeBatchNight;
                existingPayment.CreditCardTidMorning = payment.CreditCardTidMorning;
                existingPayment.CreditCardBatchMorning = payment.CreditCardBatchMorning;
                existingPayment.CreditCardTidNight = payment.CreditCardTidNight;
                existingPayment.CreditCardBatchNight = payment.CreditCardBatchNight;
                existingPayment.PetroCardTidMorning = payment.PetroCardTidMorning;
                existingPayment.PetroCardBatchMorning = payment.PetroCardBatchMorning;
                existingPayment.PetroCardTidNight = payment.PetroCardTidNight;
                existingPayment.PetroCardBatchNight = payment.PetroCardBatchNight;
                existingPayment.DynamicItemsJson = payment.DynamicItemsJson;
            }
            else
            {
                payment.DsmEntryId = savedEntryId;
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
            var existingPD = await context.Set<DsmPersonalDebtor>().Where(p => p.DsmEntryId == savedEntryId).ToListAsync();
            context.Set<DsmPersonalDebtor>().RemoveRange(existingPD);
            if (personalDebtors != null)
            {
                foreach (var p in personalDebtors)
                {
                    p.DsmEntryId = savedEntryId;
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
                    kp.DsmName = dsmName;
                    kp.Date = shift.ShiftDate;
                }
                context.Set<KhandharePetroleumEntry>().AddRange(khandharePetroleumEntries);
            }

            // Save Cross-DSM QR Payments
            var existingQr = await context.Set<DsmQrPaymentEntry>().Where(q => q.DsmEntryId == savedEntryId).ToListAsync();
            context.Set<DsmQrPaymentEntry>().RemoveRange(existingQr);
            if (qrPayments != null)
            {
                foreach (var q in qrPayments)
                {
                    q.DsmEntryId = savedEntryId;
                    q.DsmName = dsmName;
                    q.Date = shift.ShiftDate;
                }
                context.Set<DsmQrPaymentEntry>().AddRange(qrPayments);
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
                .Include(e => e.QrPayments)
                .FirstOrDefaultAsync(e => e.DsmEntryId == savedEntryId);

            var shiftEntries = await context.Set<DsmEntry>()
                .Include(e => e.TestingEntries)
                .Where(e => e.ShiftId == shift.ShiftId)
                .ToListAsync();

            if (fullEntry != null)
            {
                var calc = _dsmCalculationService.Calculate(ToCalculationDto(fullEntry));
                var totalConnectedGross = (decimal)effectiveConnectedPumps
                    .Sum(slavePumpId => readingsByPump.TryGetValue(slavePumpId, out var sr) ? sr.Sum(x => x.Amount) : 0);

                var slaveTesting = (decimal)effectiveConnectedPumps.Sum(spId =>
                    shiftEntries.Where(e => e.PumpId == spId && e.TestingEntries != null)
                                .SelectMany(e => e.TestingEntries)
                                .Sum(t => t.Amount));
                var groupTesting = (fullEntry.TestingEntries?.Sum(t => (decimal)t.Amount) ?? 0m) + slaveTesting;

                entry.GrossSales = calc.GrossSales + totalConnectedGross;
                entry.TotalInDirect = calc.TotalInDirect;
                entry.TotalCreditors = calc.TotalCreditors;
                entry.TotalCollection = calc.TotalCollection;
                var netGrossSales = entry.GrossSales - groupTesting;
                entry.Mismatch = calc.TotalCollection - netGrossSales;
                entry.UpdatedAt = DateTime.Now;

                // Automatic DSM Loss (Personal Debtor) handling based on shortage (no tolerance)
                double totalShortage = entry.Mismatch < 0 ? (double)Math.Abs(entry.Mismatch) : 0;
                double dsmLossAmount = totalShortage;
                var existingAutoLoss = context.Set<DsmPersonalDebtor>().Local
                    .FirstOrDefault(p => p.DsmEntryId == savedEntryId && p.Remarks != null && p.Remarks.Contains("Shortage"))
                    ?? await context.Set<DsmPersonalDebtor>()
                        .FirstOrDefaultAsync(p => p.DsmEntryId == savedEntryId && p.Remarks != null && p.Remarks.Contains("Shortage"));
                if (dsmLossAmount > 0)
                {
                    if (existingAutoLoss == null)
                    {
                        var autoLoss = new DsmPersonalDebtor
                        {
                            DsmEntryId = savedEntryId,
                            DsmName = dsmName,
                            Date = shift.ShiftDate,
                            Time = DateTime.Now.ToString("hh:mm tt"),
                            Amount = dsmLossAmount,
                            Remarks = $"Auto Shift Shortage (Pump {pumpId}, Shift {shiftType})",
                            PaymentMethod = "Cash"
                        };
                        context.Set<DsmPersonalDebtor>().Add(autoLoss);
                    }
                    else
                    {
                        existingAutoLoss.Amount = dsmLossAmount;
                        context.Entry(existingAutoLoss).State = EntityState.Modified;
                    }
                }
                else if (existingAutoLoss != null)
                {
                    context.Set<DsmPersonalDebtor>().Remove(existingAutoLoss);
                }

                context.Entry(entry).State = EntityState.Modified;
                await context.SaveChangesAsync();
            }

            // Save connected pump entries (slaves)
            shiftEntries = await context.Set<DsmEntry>()
                .Where(e => e.ShiftId == shift.ShiftId)
                .ToListAsync();
            var activeSlaveEntryIds = new HashSet<int>();

            foreach (var slavePumpId in effectiveConnectedPumps)
            {
                var existingConnectedEntry = shiftEntries.FirstOrDefault(e =>
                    entry.DsmEntryId != 0 && e.ReconciledToPumpId == entry.DsmEntryId && e.PumpId == slavePumpId);

                DsmEntry connectedEntry;
                if (existingConnectedEntry != null)
                {
                    existingConnectedEntry.ReconciledToPumpId = entry.DsmEntryId;
                    existingConnectedEntry.DsmName = dsmName;
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
                        PumpId = slavePumpId,
                        ReconciledToPumpId = entry.DsmEntryId,
                        StartTime = startTime,
                        EndTime = endTime,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };
                    context.Set<DsmEntry>().Add(connectedEntry);
                }

                await context.SaveChangesAsync();
                var savedConnEntryId = connectedEntry.DsmEntryId;
                activeSlaveEntryIds.Add(savedConnEntryId);

                // Save nozzle readings for connected pump
                var existingConnNozzles = await context.Set<NozzleReading>().Where(r => r.DsmEntryId == savedConnEntryId).ToListAsync();
                context.Set<NozzleReading>().RemoveRange(existingConnNozzles);
                var slaveReadings = readingsByPump.TryGetValue(slavePumpId, out var sr) ? sr : new List<NozzleReading>();
                foreach (var r in slaveReadings)
                {
                    r.DsmEntryId = savedConnEntryId;
                    r.DsmEntry = null;
                    r.SaleLitres = r.ClosingReading - r.OpeningReading;
                    r.Amount = r.SaleLitres * r.Rate;
                }
                context.Set<NozzleReading>().AddRange(slaveReadings);

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
                    connectedEntry.TotalInDirect = 0m;
                    connectedEntry.TotalCreditors = 0m;
                    connectedEntry.TotalCollection = 0m;
                    connectedEntry.Mismatch = 0m;
                    connectedEntry.UpdatedAt = DateTime.Now;
                    context.Entry(connectedEntry).State = EntityState.Modified;
                    await context.SaveChangesAsync();
                }
            }

            // Remove orphaned slave entries
            var orphanedSlaves = shiftEntries.Where(e =>
                entry.DsmEntryId != 0
                && e.ReconciledToPumpId == entry.DsmEntryId
                && !activeSlaveEntryIds.Contains(e.DsmEntryId)).ToList();

            if (orphanedSlaves.Count > 0)
            {
                context.Set<DsmEntry>().RemoveRange(orphanedSlaves);
                await context.SaveChangesAsync();
            }

            // Sync ConnectedPumpId and ConnectedPumpIdsJson on saved primary entry
            var targetConnId = effectiveConnectedPumps.FirstOrDefault() > 0 ? effectiveConnectedPumps.FirstOrDefault() : (int?)null;
            var targetConnJson = effectiveConnectedPumps.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(effectiveConnectedPumps) : null;
            if (entry.ConnectedPumpId != targetConnId || entry.ConnectedPumpIdsJson != targetConnJson)
            {
                entry.ConnectedPumpId = targetConnId;
                entry.ConnectedPumpIdsJson = targetConnJson;
                context.Entry(entry).State = EntityState.Modified;
                await context.SaveChangesAsync();
            }

            // Propagate nozzle readings downstream
            await PropagateNozzleReadingsWithContextAsync(context, date, shiftType);

            _logger.Information("DSM entry saved successfully via transaction context: {DsmName} Pump {PumpId} with {SlaveCount} slaves", dsmName, pumpId, effectiveConnectedPumps.Count);
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
        // Saved entries are finalized upon submission and must not be retroactively mutated.
        await Task.CompletedTask;
        return Result.Ok();
    }
}


