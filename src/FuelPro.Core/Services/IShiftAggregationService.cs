using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;

namespace FuelPro.Core.Services;

/// <summary>
/// Aggregates DSM entry data at the shift level for Final Calculation tables.
/// </summary>
public interface IShiftAggregationService
{
    List<DsmSummaryRowDto> BuildDsmSummaryRows(List<DsmEntry> entries);
    DsmSummaryRowDto BuildDsmSummaryTotalRow(List<DsmSummaryRowDto> rows);
    CashAggregateDto AggregateCash(List<DsmEntry> entries, string cashType);
    List<DebitRegisterRowDto> BuildCreditorRows(List<DsmEntry> entries);
    List<ExpenseRegisterRowDto> BuildExpenseRows(List<DsmEntry> entries, List<Expense> shiftExpenses);

    (double litres, double amount) GetFuelTotals(List<DsmEntry> entries, string fuelType, double? overrideRate);
    double GetTotalLitresByFuelType(List<NozzleReading> allReadings, string fuelType);

    List<ReconciliationRowDto> BuildReconciliationRows(
        double msTesting, double hsdTesting, double hsdTesting2, double cngTesting, double phonePeCardMorning, double phonePeCardNight, double phonePeMorning, double phonePeNight, double petroCard,
        double debit, double creditCardMorning, double creditCardNight, double bankCash, double cashInHand,
        double expenses);

    double CalculateDifference(double grossSaleFuel, double reconciliationTotal);
}
