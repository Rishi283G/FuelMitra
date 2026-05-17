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
    private readonly ILogger _logger = Log.ForContext<ShiftCalculationService>();

    public ShiftCalculationService(
        IDsmEntryRepository dsmRepo,
        IShiftRepository shiftRepo,
        IExpenseRepository expenseRepo,
        IDsmCalculationService dsmCalculationService)
    {
        _dsmRepo = dsmRepo;
        _shiftRepo = shiftRepo;
        _expenseRepo = expenseRepo;
        _dsmCalculationService = dsmCalculationService;
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
            foreach (var entry in entries)
            {
                var cash1 = entry.CashDenominations.Where(c => c.CashType == "Cash1").Sum(c => c.TotalAmount);
                var cash2 = entry.CashDenominations.Where(c => c.CashType == "Cash2").Sum(c => c.TotalAmount);
                var calc = _dsmCalculationService.Calculate(new DsmEntryDto
                {
                    DSMEntryId = entry.DsmEntryId,
                    NozzleReadings = entry.NozzleReadings.Select(n => new NozzleReadingDto { Amount = (decimal)n.Amount }).ToList(),
                    PaymentCollection = new PaymentCollectionDto
                    {
                        PhonePe = (decimal)((entry.PaymentCollection?.PhonePe ?? 0) + (entry.PaymentCollection?.PhonePeCard ?? 0)),
                        CreditCard = (decimal)((entry.PaymentCollection?.CreditCard ?? 0) + (entry.PaymentCollection?.PetroCard ?? 0)),
                        CashDeposit = (decimal)(cash1 + cash2 + (entry.PaymentCollection?.CashDeposit ?? 0)),
                        PhysicalCash = 0  // Others is informational only, not included in TotalInDirect
                    },
                    DebitEntries = entry.DebitEntries.Select(d => new DebitEntryDto { Amount = (decimal)d.Amount }).ToList(),
                    TestingEntries = entry.TestingEntries.Select(t => new TestingEntryDto
                    {
                        FuelType = t.FuelType,
                        Amount = (decimal)t.Amount
                    }).ToList()
                });

                dto.DsmSummaryRows.Add(new DsmSummaryRowDto
                {
                    DsmName = entry.DsmName,
                    PumpId = entry.PumpId,
                    PhonePeCard = entry.PaymentCollection?.PhonePeCard ?? 0,
                    PhonePeCardMorning = entry.PaymentCollection?.PhonePeCardMorning ?? 0,
                    PhonePeCardNight = entry.PaymentCollection?.PhonePeCardNight ?? 0,
                    PhonePe = (double)((entry.PaymentCollection?.PhonePe ?? 0) + (entry.PaymentCollection?.PhonePeCard ?? 0)),
                    PhonePeMorning = entry.PaymentCollection?.PhonePeMorning ?? 0,
                    PhonePeNight = entry.PaymentCollection?.PhonePeNight ?? 0,
                    CreditCardMorning = entry.PaymentCollection?.CreditCardMorning ?? 0,
                    CreditCardNight = entry.PaymentCollection?.CreditCardNight ?? 0,
                    PetroCard = entry.PaymentCollection?.PetroCard ?? 0,
                    Debit = (double)calc.TotalCreditors,
                    Expenses = entry.Expenses.Sum(e => e.Amount),
                    Testing = entry.TestingEntries.Sum(t => t.Amount),
                    CashDeposit = cash1,
                    CashInHand = cash2
                });
            }

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

            // TABLE E — Fuel Dispensed
            var allReadings = entries.SelectMany(e => e.NozzleReadings).ToList();

            var hsdReadings = allReadings.Where(r => r.FuelType == "HSD").ToList();
            dto.HsdLitres = hsdReadings.Sum(r => r.SaleLitres);
            dto.HsdRate = hsdReadings.FirstOrDefault()?.Rate ?? 0;
            dto.HsdAmount = hsdReadings.Sum(r => r.Amount);

            var msIReadings = allReadings.Where(r => r.FuelType == "MS-I").ToList();
            dto.MsILitres = msIReadings.Sum(r => r.SaleLitres);
            dto.MsIRate = msIReadings.FirstOrDefault()?.Rate ?? 0;
            dto.MsIAmount = msIReadings.Sum(r => r.Amount);

            var msIIReadings = allReadings.Where(r => r.FuelType == "MS-II").ToList();
            dto.MsIILitres = msIIReadings.Sum(r => r.SaleLitres);
            dto.MsIIRate = msIIReadings.FirstOrDefault()?.Rate ?? 0;
            dto.MsIIAmount = msIIReadings.Sum(r => r.Amount);

            dto.TotalLitres = dto.HsdLitres + dto.MsILitres + dto.MsIILitres;
            dto.TotalFuelSaleAmount = dto.HsdAmount + dto.MsIAmount + dto.MsIIAmount;
            dto.TotalMsDispensed = dto.MsILitres + dto.MsIILitres;

            // TABLE F — Final Reconciliation
            dto.MsTesting = entries.SelectMany(e => e.TestingEntries).Where(t => t.FuelType == "MS").Sum(t => t.Amount);
            dto.HsdTesting = entries.SelectMany(e => e.TestingEntries).Where(t => t.FuelType == "HSD").Sum(t => t.Amount);
            dto.PhonePeTotal = dto.DsmSummaryRows.Sum(r => r.PhonePeCard + r.PhonePeMorning + r.PhonePeNight);
            dto.PhonePeCardTotal = dto.DsmSummaryRows.Sum(r => r.PhonePeCard);
            dto.PhonePeCardMorningTotal = dto.DsmSummaryRows.Sum(r => r.PhonePeCardMorning);
            dto.PhonePeCardNightTotal = dto.DsmSummaryRows.Sum(r => r.PhonePeCardNight);
            dto.PhonePeMorningTotal = dto.DsmSummaryRows.Sum(r => r.PhonePeMorning);
            dto.PhonePeNightTotal = dto.DsmSummaryRows.Sum(r => r.PhonePeNight);
            dto.PetroCardTotal = dto.DsmSummaryRows.Sum(r => r.PetroCard);
            dto.CreditCardMorningTotal = dto.DsmSummaryRows.Sum(r => r.CreditCardMorning);
            dto.CreditCardNightTotal = dto.DsmSummaryRows.Sum(r => r.CreditCardNight);
            dto.CreditCardTotal = dto.CreditCardMorningTotal + dto.CreditCardNightTotal;
            dto.BankCash = dto.Cash1Aggregate.GrandTotal;
            dto.CashInHand = dto.Cash2Aggregate.GrandTotal;

            dto.TotalAmounts = dto.MsTesting + dto.HsdTesting + dto.PhonePeTotal + dto.PetroCardTotal + dto.CreditCardTotal + dto.TotalDebit
                + dto.BankCash + dto.CashInHand;

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
                TotalFuelSale = calc.TotalFuelSaleAmount,
                TotalCash = calc.BankCash + calc.CashInHand,
                TotalDigitalPayments = calc.PhonePeTotal + calc.PetroCardTotal + calc.CreditCardTotal,
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
