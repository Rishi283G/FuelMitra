using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using Serilog;

namespace FuelPro.Core.Services;

/// <summary>
/// Aggregates all DSM entries for a shift into final calculation tables.
/// </summary>
public class ShiftCalculationService
{
    private readonly IDsmEntryRepository _dsmRepo;
    private readonly IShiftRepository _shiftRepo;
    private readonly IExpenseRepository _expenseRepo;
    private readonly IDsmCalculationService _dsmCalculationService;
    private readonly ITidCalculationService _tidService;
    private readonly IShiftAggregationService _aggregationService;
    private readonly ILogger _logger = Log.ForContext<ShiftCalculationService>();

    public ShiftCalculationService(
        IDsmEntryRepository dsmRepo,
        IShiftRepository shiftRepo,
        IExpenseRepository expenseRepo,
        IDsmCalculationService dsmCalculationService,
        ITidCalculationService tidService,
        IShiftAggregationService? aggregationService = null)
    {
        _dsmRepo = dsmRepo;
        _shiftRepo = shiftRepo;
        _expenseRepo = expenseRepo;
        _dsmCalculationService = dsmCalculationService;
        _tidService = tidService;
        _aggregationService = aggregationService ?? new ShiftAggregationService();
    }

    /// <summary>
    /// Generates complete final calculation for a shift.
    /// </summary>
    public async Task<Result<FinalCalculationDto>> CalculateAsync(DateTime date, string shiftType)
    {
        try
        {
            var shiftResult = await _shiftRepo.GetShiftAsync(date, shiftType);
            if (!shiftResult.Success || shiftResult.Data == null)
                return Result<FinalCalculationDto>.Fail("Shift not found. No entries exist for this date and shift.");

            var shift = shiftResult.Data;
            var entriesResult = await _dsmRepo.GetEntriesForShiftAsync(shift.ShiftId);
            if (!entriesResult.Success) return Result<FinalCalculationDto>.Fail(entriesResult.Error);

            var entries = entriesResult.Data!;
            var primaryEntries = entries
                .Where(e => !e.ReconciledToPumpId.HasValue)
                .OrderBy(e => e.DsmEntryId)
                .ToList();
            var shiftExpensesResult = await _expenseRepo.GetByShiftIdAsync(shift.ShiftId);
            var shiftExpenses = shiftExpensesResult.Success ? shiftExpensesResult.Data! : new List<Expense>();

            var dto = new FinalCalculationDto
            {
                ShiftId = shift.ShiftId,
                ShiftDate = shift.ShiftDate,
                ShiftType = shift.ShiftType,
                IsLocked = shift.IsLocked
            };

            // TABLE A — DSM Summary
            dto.DsmSummaryRows = _aggregationService.BuildDsmSummaryRows(primaryEntries);

            // Group by DSM Name for shift-level totals using centralized ShiftAggregationService
            dto.DsmShiftTotals = _aggregationService.BuildDsmShiftTotals(dto.DsmSummaryRows);

            // TABLE B — Cash Aggregates
            dto.Cash1Aggregate = AggregateCash(entries, "Cash1");
            dto.Cash2Aggregate = AggregateCash(entries, "Cash2");

            // TABLE C — Debit Register
            foreach (var entry in entries)
            {
                foreach (var debit in entry.DebitEntries)
                {
                    dto.DebitRegisterRows.Add(new DebitRegisterRowDto
                    {
                        DsmName = entry.DsmName,
                        DebtorName = debit.DebtorName,
                        Amount = debit.Amount
                    });
                }
            }
            dto.TotalDebit = dto.DebitRegisterRows.Sum(r => r.Amount);

            // TABLE D — Expenses Register
            foreach (var entry in entries)
            {
                foreach (var expense in entry.Expenses)
                {
                    dto.ExpenseRegisterRows.Add(new ExpenseRegisterRowDto
                    {
                        ExpenseId = expense.ExpenseId,
                        DsmName = entry.DsmName,
                        Description = expense.Description,
                        Amount = expense.Amount,
                        IsShiftLevel = false
                    });
                }
            }
            foreach (var expense in shiftExpenses)
            {
                dto.ExpenseRegisterRows.Add(new ExpenseRegisterRowDto
                {
                    ExpenseId = expense.ExpenseId,
                    DsmName = "— Shift —",
                    Description = expense.Description,
                    Amount = expense.Amount,
                    IsShiftLevel = true
                });
            }
            dto.TotalExpenses = dto.ExpenseRegisterRows.Sum(r => r.Amount);

            // TABLE E — Fuel Dispensed (classify by nozzle number and pump, not stored FuelType)
            var readingsWithPump = entries.SelectMany(e => e.NozzleReadings.Select(r => new { e.PumpId, Reading = r })).ToList();

            var hsdReadings = readingsWithPump.Where(x => PumpConfiguration.GetFuelTypeDisplayName(x.PumpId, x.Reading.NozzleNumber, shift.ShiftDate) == "HSD").Select(x => x.Reading).ToList();
            dto.HsdLitres = hsdReadings.Sum(r => r.SaleLitres);
            dto.HsdRate = hsdReadings.FirstOrDefault()?.Rate ?? 0;
            dto.HsdAmount = hsdReadings.Sum(r => r.Amount);

            var msIReadings = readingsWithPump.Where(x => PumpConfiguration.GetFuelTypeDisplayName(x.PumpId, x.Reading.NozzleNumber, shift.ShiftDate) == "MS-I").Select(x => x.Reading).ToList();
            dto.MsILitres = msIReadings.Sum(r => r.SaleLitres);
            dto.MsIRate = msIReadings.FirstOrDefault()?.Rate ?? 0;
            dto.MsIAmount = msIReadings.Sum(r => r.Amount);

            var msIIReadings = readingsWithPump.Where(x => PumpConfiguration.GetFuelTypeDisplayName(x.PumpId, x.Reading.NozzleNumber, shift.ShiftDate) == "MS-II").Select(x => x.Reading).ToList();
            dto.MsIILitres = msIIReadings.Sum(r => r.SaleLitres);
            dto.MsIIRate = msIIReadings.FirstOrDefault()?.Rate ?? 0;
            dto.MsIIAmount = msIIReadings.Sum(r => r.Amount);

            var cngReadings = readingsWithPump.Where(x => PumpConfiguration.GetFuelTypeDisplayName(x.PumpId, x.Reading.NozzleNumber, shift.ShiftDate) == "CNG").Select(x => x.Reading).ToList();
            dto.CngLitres = cngReadings.Sum(r => r.SaleLitres);
            dto.CngRate = cngReadings.FirstOrDefault()?.Rate ?? 0;
            dto.CngAmount = cngReadings.Sum(r => r.Amount);

            dto.TotalLitres = dto.HsdLitres + dto.MsILitres + dto.MsIILitres + dto.CngLitres;
            dto.TotalFuelSaleAmount = dto.HsdAmount + dto.MsIAmount + dto.MsIIAmount + dto.CngAmount;
            dto.TotalMsDispensed = dto.MsILitres + dto.MsIILitres;

            // TABLE F — Final Reconciliation
            double msTesting = 0;
            double hsdTesting = 0;
            double cngTesting = 0;

            foreach (var entry in entries)
            {
                foreach (var t in entry.TestingEntries)
                {
                    var cat = PumpConfiguration.GetTestingTankCategory(t.FuelType, entry.PumpId, shift.ShiftDate.Date);
                    if (cat == "MS") msTesting += (double)t.Amount;
                    else if (cat == "HSD" || cat == "HSD-II") hsdTesting += (double)t.Amount;
                    else if (cat == "CNG") cngTesting += (double)t.Amount;
                }
            }

            dto.MsTesting = msTesting;
            dto.HsdTesting = hsdTesting;
            dto.CngTesting = cngTesting;

            var tidSheetToday = await _tidService.GetTidSheetAsync(shift.ShiftDate.Date);
            var tidSheetTomorrow = await _tidService.GetTidSheetAsync(shift.ShiftDate.Date.AddDays(1));

            if (shift.ShiftType == "A")
            {
                dto.PhonePeCardMorningTotal = tidSheetToday.PhonePeCardDay;
                dto.PhonePeCardNightTotal = 0;
                dto.PhonePeCardTotal = tidSheetToday.PhonePeCardDay;

                dto.PhonePeMorningTotal = tidSheetToday.PhonePeDirectDay;
                dto.PhonePeNightTotal = 0;
                dto.PhonePeTotal = tidSheetToday.PhonePeDirectDay + tidSheetToday.PhonePeCardDay;

                dto.PetroCardMorningTotal = tidSheetToday.PetroCardDay;
                dto.PetroCardNightTotal = 0;
                dto.PetroCardTotal = tidSheetToday.PetroCardDay;

                dto.CreditCardMorningTotal = tidSheetToday.PineLabsCardDay;
                dto.CreditCardNightTotal = 0;
                dto.CreditCardTotal = tidSheetToday.PineLabsCardDay;
            }
            else
            {
                dto.PhonePeCardMorningTotal = tidSheetTomorrow.PhonePeCardMorning;
                dto.PhonePeCardNightTotal = tidSheetToday.PhonePeCardNight;
                dto.PhonePeCardTotal = tidSheetTomorrow.PhonePeCardMorning + tidSheetToday.PhonePeCardNight;

                dto.PhonePeMorningTotal = tidSheetTomorrow.PhonePeDirectMorning;
                dto.PhonePeNightTotal = tidSheetToday.PhonePeDirectNight;
                dto.PhonePeTotal = dto.PhonePeMorningTotal + dto.PhonePeNightTotal + dto.PhonePeCardTotal;

                dto.PetroCardMorningTotal = tidSheetTomorrow.PetroCardMorning;
                dto.PetroCardNightTotal = tidSheetToday.PetroCardNight;
                dto.PetroCardTotal = dto.PetroCardMorningTotal + dto.PetroCardNightTotal;

                dto.CreditCardMorningTotal = tidSheetTomorrow.PineLabsCardMorning;
                dto.CreditCardNightTotal = tidSheetToday.PineLabsCardNight;
                dto.CreditCardTotal = dto.CreditCardMorningTotal + dto.CreditCardNightTotal;
            }

            var oilDefResult = ReportService.ExtractOilDefSales(entries);
            dto.OilDefSales = oilDefResult.Rows;
            dto.OilDefSalesTotal = oilDefResult.Total;

            dto.BankCash = dto.Cash1Aggregate.GrandTotal;
            dto.CashInHand = dto.Cash2Aggregate.GrandTotal;

            double dynamicPaymentsTotal = dto.DsmSummaryRows.Sum(r => r.DynamicCollectionsTotal);
            dto.TotalAmounts = dto.MsTesting + dto.HsdTesting + dto.CngTesting + dto.PhonePeTotal + dto.PetroCardTotal + dto.CreditCardTotal + dynamicPaymentsTotal + dto.TotalDebit
                + dto.BankCash + dto.CashInHand + dto.TotalExpenses;

            dto.ReconciliationDifference = dto.TotalFuelSaleAmount - dto.TotalAmounts;

            return Result<FinalCalculationDto>.Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to calculate shift totals");
            return Result<FinalCalculationDto>.Fail($"Calculation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates dashboard summary for a shift.
    /// </summary>
    public async Task<Result<ShiftSummaryDto>> GetShiftSummaryAsync(DateTime date, string shiftType)
    {
        try
        {
            var calcResult = await CalculateAsync(date, shiftType);
            if (!calcResult.Success)
                return Result<ShiftSummaryDto>.Ok(new ShiftSummaryDto { ShiftDate = date, ShiftType = shiftType });

            var calc = calcResult.Data!;
            return Result<ShiftSummaryDto>.Ok(new ShiftSummaryDto
            {
                ShiftDate = calc.ShiftDate,
                ShiftType = calc.ShiftType,
                TotalDsmEntries = calc.DsmSummaryRows.Count,
                TotalHsdLitres = calc.HsdLitres,
                TotalMsILitres = calc.MsILitres,
                TotalMsIILitres = calc.MsIILitres,
                TotalCngLitres = calc.CngLitres,
                TotalFuelSale = calc.TotalFuelSaleAmount,
                TotalCash = calc.BankCash + calc.CashInHand,
                TotalDigitalPayments = calc.PhonePeTotal + calc.PetroCardTotal + calc.CreditCardTotal + calc.DsmSummaryRows.Sum(r => r.DynamicCollectionsTotal),
                TotalDebit = calc.TotalDebit,
                TotalExpenses = calc.TotalExpenses,
                IsLocked = calc.IsLocked
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get shift summary");
            return Result<ShiftSummaryDto>.Fail($"Failed to load summary: {ex.Message}");
        }
    }

    private static CashAggregateDto AggregateCash(List<DsmEntry> entries, string cashType)
    {
        var denoms = entries
            .SelectMany(e => e.CashDenominations)
            .Where(c => c.CashType == cashType)
            .ToList();

        return new CashAggregateDto
        {
            Total500 = denoms.Sum(d => d.Denom500),
            Total200 = denoms.Sum(d => d.Denom200),
            Total100 = denoms.Sum(d => d.Denom100),
            Total50 = denoms.Sum(d => d.Denom50),
            Total20 = denoms.Sum(d => d.Denom20),
            Total10 = denoms.Sum(d => d.Denom10),
            TotalCoins = denoms.Sum(d => d.Coins),
            GrandTotal = denoms.Sum(d => d.TotalAmount)
        };
    }
}
