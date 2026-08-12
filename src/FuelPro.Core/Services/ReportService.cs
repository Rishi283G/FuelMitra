using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using Serilog;

namespace FuelPro.Core.Services;

public class ReportService : IReportService
{
    private readonly IShiftAggregationService _aggregation;
    private readonly ILogger _logger = Log.ForContext<ReportService>();

    public ReportService(IShiftAggregationService aggregation)
    {
        _aggregation = aggregation;
    }

    public ShiftReportDto CalculateShiftReport(
        DateTime date,
        string shiftType,
        List<DsmEntry> entries,
        List<Expense> shiftExpenses,
        List<ShiftOtherCash> otherCashList,
        List<CreditorRepayment> repayments,
        double hsdRate, double msIRate, double msIIRate, double cngRate,
        BusinessDayTidSheet todayTid, BusinessDayTidSheet tomorrowTid,
        string stationName)
    {
        var dto = new ShiftReportDto
        {
            Date = date,
            DateString = date.ToString("dd/MM/yyyy"),
            ShiftLabel = shiftType == "A" ? "I" : shiftType == "B" ? "II" : shiftType == "C" ? "III" : shiftType,
            StationName = stationName
        };

        // 1. Fuel Sales (Table E)
        double hsdL = 0, hsdA = 0;
        double msIL = 0, msIA = 0;
        double msIIL = 0, msIIA = 0;
        double cngL = 0, cngA = 0;

        var entriesList = MergeConnectedPumpEntries(entries ?? new List<DsmEntry>());
        (hsdL, hsdA) = _aggregation.GetFuelTotals(entriesList, "HSD", null);
        (msIL, msIA) = _aggregation.GetFuelTotals(entriesList, "MS-I", null);
        (msIIL, msIIA) = _aggregation.GetFuelTotals(entriesList, "MS-II", null);
        (cngL, cngA) = _aggregation.GetFuelTotals(entriesList, "CNG", null);

        if (hsdA == 0 && hsdL > 0) hsdA = hsdL * hsdRate;
        if (msIIA == 0 && msIIL > 0) msIIA = msIIL * msIIRate;
        if (msIA == 0 && msIL > 0) msIA = msIL * msIRate;
        if (cngA == 0 && cngL > 0) cngA = cngL * cngRate;

        dto.FuelSales = new List<FuelSaleRowDto>();
        if (hsdL > 0 || hsdA > 0)
            dto.FuelSales.Add(new FuelSaleRowDto { Description = "HSD - 20KL", FuelType = "HSD", Litres = hsdL, Rate = hsdL > 0 ? Math.Round(hsdA / hsdL, 2) : hsdRate, Amount = hsdA });
        if (msIIL > 0 || msIIA > 0)
            dto.FuelSales.Add(new FuelSaleRowDto { Description = "HSD - 20KL II", FuelType = "MS-II", Litres = msIIL, Rate = msIIL > 0 ? Math.Round(msIIA / msIIL, 2) : msIIRate, Amount = msIIA });
        if (msIL > 0 || msIA > 0)
            dto.FuelSales.Add(new FuelSaleRowDto { Description = "MS - 20KL", FuelType = "MS-I", Litres = msIL, Rate = msIL > 0 ? Math.Round(msIA / msIL, 2) : msIRate, Amount = msIA });
        if (cngL > 0 || cngA > 0)
            dto.FuelSales.Add(new FuelSaleRowDto { Description = "CNG - Line", FuelType = "CNG", Litres = cngL, Rate = cngL > 0 ? Math.Round(cngA / cngL, 2) : cngRate, Amount = cngA });

        dto.TotalFuelLitres = dto.FuelSales.Sum(f => f.Litres);
        // Use actual nozzle reading amounts to ensure ExpectedCollection matches DSM Summary Gross Sales
        dto.TotalFuelAmount = dto.FuelSales.Sum(f => f.Amount);
        dto.OtherCashTotal = otherCashList != null ? otherCashList.Sum(o => o.Amount) : 0;
        dto.GrandTotalSaleAmount = dto.TotalFuelAmount;

        // 2. DSM Summary (Table A)
        var summaryRows = _aggregation.BuildDsmSummaryRows(entriesList);
        dto.DsmSummaryRows = summaryRows;
        dto.DsmSummaryTotals = _aggregation.BuildDsmSummaryTotalRow(summaryRows);

        // 3. Cash Summary (Table B)
        dto.Cash1 = _aggregation.AggregateCash(entriesList, "Cash1");
        dto.Cash2 = _aggregation.AggregateCash(entriesList, "Cash2");

        // 4. Debtors (Table C)
        dto.CreditorRows = _aggregation.BuildCreditorRows(entriesList);
        dto.CreditorsTotal = dto.CreditorRows.Sum(c => c.Amount);

        // 5. Expenses (Table D)
        var shiftExpensesList = shiftExpenses ?? new List<Expense>();
        dto.ExpenseRows = _aggregation.BuildExpenseRows(entriesList, shiftExpensesList);
        dto.ExpensesTotal = dto.ExpenseRows.Where(r => !r.IsShiftLevel).Sum(e => e.Amount);

        // 6. Debtor Repayments
        var repaymentsList = new List<CreditorRepayment>();
        if (repayments != null)
        {
            foreach (var r in repayments)
            {
                var classified = SettlementWindowResolver.Classify(r);
                if (classified.IsValid && classified.BusinessDate == date.Date)
                {
                    bool match = false;
                    if (shiftType == "B" && classified.SettlementWindow == "Day")
                    {
                        match = true;
                    }
                    else if (shiftType == "A" && (classified.SettlementWindow == "Morning" || classified.SettlementWindow == "Night"))
                    {
                        match = true;
                    }

                    if (match)
                    {
                        repaymentsList.Add(r);
                    }
                }
            }
        }
        dto.DebtorRepaymentsTotal = repaymentsList.Sum(r => r.Amount);
        foreach (var r in repaymentsList)
        {
            string mode = r.PaymentMode ?? "";
            if (string.Equals(mode, "Cash", StringComparison.OrdinalIgnoreCase)) dto.CashRepayments += r.Amount;
            else if (string.Equals(mode, "PhonePe", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "PhonePe UPI", StringComparison.OrdinalIgnoreCase)) dto.PhonePeRepayments += r.Amount;
            else if (string.Equals(mode, "Credit Card", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "PineLabs Card", StringComparison.OrdinalIgnoreCase)) dto.CreditCardRepayments += r.Amount;
            else if (string.Equals(mode, "PetroCard", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "Petro Card", StringComparison.OrdinalIgnoreCase)) dto.PetroCardRepayments += r.Amount;
            else if (string.Equals(mode, "Bank Transfer", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "Cheque", StringComparison.OrdinalIgnoreCase)) dto.BankCashRepayments += r.Amount;

            string refNo = "";
            if (r.PaymentMode == "PhonePe" || r.PaymentMode == "Credit Card" || r.PaymentMode == "PineLabs Card" || r.PaymentMode == "PetroCard" || r.PaymentMode == "Bank Transfer")
                refNo = $"TID: {r.CardTid}, Batch: {r.CardBatch}";
            else if (r.PaymentMode == "Cheque")
                refNo = $"Chq: {r.ChequeNo}";

            dto.DebtorRepayments.Add(new CreditorRepaymentPrintDto
            {
                DebtorName = r.CreditorName,
                PaymentMode = r.PaymentMode,
                RefNo = refNo,
                Amount = (decimal)r.Amount
            });
        }

        // Build generic RepaymentBreakdown
        dto.RepaymentBreakdown = repaymentsList
            .GroupBy(r => r.PaymentMode ?? "Unknown")
            .Select(g => new RepaymentBreakdownDto
            {
                PaymentMethod = g.Key,
                Amount = g.Sum(x => x.Amount),
                IsReconcilable = IsReconcilableMode(g.Key)
            }).ToList();

        double reconcilableRecoveriesTotal = dto.CashRepayments + dto.PhonePeRepayments
            + dto.CreditCardRepayments + dto.PetroCardRepayments;

        // 7. Oil & DEF Sales (Phase 3/4 Product Sales)
        var oilDefResult = ExtractOilDefSales(entriesList);
        dto.OilDefSales = oilDefResult.Rows;
        dto.OilDefSalesTotal = oilDefResult.Total;

        // 8. Aggregated Digital Collections from DB Entries
        double phonePeMorning = 0;
        double phonePeNight = 0;
        double phonePeCardMorning = 0;
        double phonePeCardNight = 0;
        double creditCardMorning = 0;
        double creditCardNight = 0;
        double petroCardMorning = 0;
        double petroCardNight = 0;
        double petroCard = 0;

        foreach (var entry in entriesList)
        {
            var pc = entry.PaymentCollection;
            if (pc == null) continue;

            if (shiftType == "B")
            {
                phonePeMorning += pc.PhonePeDay;
                phonePeCardMorning += pc.PhonePeCardDay;
                creditCardMorning += pc.CreditCardDay;
                petroCardMorning += pc.PetroCardDay > 0 ? pc.PetroCardDay : pc.PetroCardMorning;
                petroCard += pc.PetroCardDay > 0 ? pc.PetroCardDay : pc.PetroCardMorning;
            }
            else
            {
                phonePeMorning += pc.PhonePeMorning;
                phonePeNight += pc.PhonePeNight;
                phonePeCardMorning += pc.PhonePeCardMorning;
                phonePeCardNight += pc.PhonePeCardNight;
                creditCardMorning += pc.CreditCardMorning;
                creditCardNight += pc.CreditCardNight;
                petroCardMorning += pc.PetroCardMorning;
                petroCardNight += pc.PetroCardNight;
                petroCard += pc.PetroCardMorning + pc.PetroCardNight;
            }
        }

        dto.PetroCardMorning = petroCardMorning + (shiftType == "B" ? dto.PetroCardRepayments : 0);
        dto.PetroCardNight = petroCardNight + (shiftType != "B" ? dto.PetroCardRepayments : 0);


        // Apply debtor repayments adjustments (silent additions per logic rules)
        double finalCashDeposit = dto.Cash1.GrandTotal;
        double finalCashInHand = dto.Cash2.GrandTotal + dto.CashRepayments;
        double finalPhonePeMorning = phonePeMorning + (shiftType == "B" ? dto.PhonePeRepayments : 0);
        double finalPhonePeNight = phonePeNight + (shiftType != "B" ? dto.PhonePeRepayments : 0);
        double finalCreditCardMorning = creditCardMorning + (shiftType == "B" ? dto.CreditCardRepayments : 0);
        double finalCreditCardNight = creditCardNight + (shiftType != "B" ? dto.CreditCardRepayments : 0);
        double finalPetroCard = petroCard + dto.PetroCardRepayments;
        double finalPhonePeCardMorning = phonePeCardMorning;
        double finalPhonePeCardNight = phonePeCardNight;

        // 9. Testing summary totals
        double msTesting = 0, msTestingVol = 0;
        double hsdTesting = 0, hsdTestingVol = 0;
        double hsdTesting2 = 0, hsdTesting2Vol = 0;
        double cngTesting = 0, cngTestingVol = 0;

        foreach (var entry in entriesList)
        {
            foreach (var t in entry.TestingEntries)
            {
                var cat = PumpConfiguration.GetTestingTankCategory(t.FuelType, entry.PumpId, date.Date);
                double tAmt = t.Amount > 0 ? (double)t.Amount : (double)(t.Litres * t.Rate);
                double tVol = (double)t.Litres;
                if (cat == "MS") { msTesting += tAmt; msTestingVol += tVol; }
                else if (cat == "HSD") { hsdTesting += tAmt; hsdTestingVol += tVol; }
                else if (cat == "HSD-II") { hsdTesting2 += tAmt; hsdTesting2Vol += tVol; }
                else if (cat == "CNG") { cngTesting += tAmt; cngTestingVol += tVol; }
            }
        }
        double testingTotal = msTesting + hsdTesting + hsdTesting2 + cngTesting;

        // 10. DSM Short calculation
        double totalDsmShort = CalculateDsmShort(entriesList);
        dto.TotalDsmShort = totalDsmShort;

        // 11. Standardized collection categories with audit breakdown (Oil & DEF sales excluded from Final Reconciliation)
        var breakdownList = new List<CollectionCategoryDto>
        {
            new() { Category = "Cash Deposit", Amount = finalCashDeposit, BaseAmount = dto.Cash1.GrandTotal, RecoveryAmount = 0 },
            new() { Category = "Cash In Hand", Amount = finalCashInHand, BaseAmount = dto.Cash2.GrandTotal, RecoveryAmount = dto.CashRepayments }
        };

        if (shiftType == "B")
        {
            breakdownList.Add(new() { Category = "PhonePe", Amount = finalPhonePeMorning + finalPhonePeNight, BaseAmount = phonePeMorning + phonePeNight, RecoveryAmount = dto.PhonePeRepayments });
            breakdownList.Add(new() { Category = "Card", Amount = finalCreditCardMorning + finalCreditCardNight, BaseAmount = creditCardMorning + creditCardNight, RecoveryAmount = dto.CreditCardRepayments });
        }
        else
        {
            breakdownList.Add(new() { Category = "PhonePe Morning", Amount = finalPhonePeMorning, BaseAmount = phonePeMorning, RecoveryAmount = 0 });
            breakdownList.Add(new() { Category = "PhonePe Night", Amount = finalPhonePeNight, BaseAmount = phonePeNight, RecoveryAmount = dto.PhonePeRepayments });
            breakdownList.Add(new() { Category = "Card Morning", Amount = finalCreditCardMorning, BaseAmount = creditCardMorning, RecoveryAmount = 0 });
            breakdownList.Add(new() { Category = "Card Night", Amount = finalCreditCardNight, BaseAmount = creditCardNight, RecoveryAmount = dto.CreditCardRepayments });
        }

        if (shiftType == "B")
        {
            breakdownList.Add(new() { Category = "Petro Card", Amount = finalPetroCard, BaseAmount = petroCard, RecoveryAmount = dto.PetroCardRepayments });
        }
        else
        {
            breakdownList.Add(new() { Category = "Petro Card Morning", Amount = dto.PetroCardMorning, BaseAmount = petroCardMorning, RecoveryAmount = 0 });
            breakdownList.Add(new() { Category = "Petro Card Night", Amount = dto.PetroCardNight, BaseAmount = petroCardNight, RecoveryAmount = dto.PetroCardRepayments });
        }
        breakdownList.Add(new() { Category = "Debtors", Amount = dto.CreditorsTotal, BaseAmount = dto.CreditorsTotal, RecoveryAmount = 0 });
        double khandhareTotal = entriesList.SelectMany(e => e.KhandharePetroleumEntries ?? new List<KhandharePetroleumEntry>()).Sum(k => k.Amount);
        breakdownList.Add(new() { Category = "Expenses", Amount = dto.ExpensesTotal, BaseAmount = dto.ExpensesTotal, RecoveryAmount = 0 });
        if (khandhareTotal > 0)
        {
            breakdownList.Add(new() { Category = "Kandhare Petroleum", Amount = khandhareTotal, BaseAmount = khandhareTotal, RecoveryAmount = 0 });
        }
        breakdownList.Add(new() { Category = "MS Testing", Amount = msTesting, BaseAmount = msTesting, RecoveryAmount = 0, Volume = msTestingVol });
        breakdownList.Add(new() { Category = "HSD Testing I", Amount = hsdTesting, BaseAmount = hsdTesting, RecoveryAmount = 0, Volume = hsdTestingVol });
        breakdownList.Add(new() { Category = "HSD Testing II", Amount = hsdTesting2, BaseAmount = hsdTesting2, RecoveryAmount = 0, Volume = hsdTesting2Vol });
        breakdownList.Add(new() { Category = "DSM Short", Amount = totalDsmShort, BaseAmount = totalDsmShort, RecoveryAmount = 0 });

        dto.CollectionBreakdown = breakdownList;

        dto.ActualCollection = dto.CollectionBreakdown.Where(c => c.Category != "DSM Short").Sum(c => c.Amount);
        dto.ExpectedCollection = dto.TotalFuelAmount + dto.OilDefSalesTotal + reconcilableRecoveriesTotal;
        dto.Difference = dto.ActualCollection - dto.ExpectedCollection;
        dto.IsBalanced = Math.Abs(dto.Difference) < 0.01;
        dto.BalancedStatus = dto.IsBalanced ? "Balanced" : (dto.Difference < 0 ? "Short" : "Excess");

        dto.PersonalDebtors = entriesList
            .SelectMany(e => e.PersonalDebtors ?? new List<DsmPersonalDebtor>())
            .Select(pd => new DsmPersonalDebtorPrintDto
            {
                DsmName = pd.DsmName,
                FuelProduct = pd.FuelProduct ?? string.Empty,
                Remarks = pd.Remarks ?? string.Empty,
                Amount = pd.Amount
            })
            .ToList();

        dto.KhandhareEntries = entriesList
            .SelectMany(e => e.KhandharePetroleumEntries ?? new List<KhandharePetroleumEntry>())
            .Select(kp => new KhandharePetroleumPrintDto
            {
                Name = kp.Name,
                VehicleNumber = kp.VehicleNumber ?? string.Empty,
                SlipNumber = kp.SlipNumber,
                Amount = kp.Amount
            })
            .ToList();

        dto.PersonalDebtorRepayments = ExtractPersonalDebtorRepayments(entriesList, date);

        return dto;
    }

    public DayReportDto CalculateDayReport(
        DateTime startDate,
        DateTime endDate,
        List<DsmEntry> entries,
        List<Expense> allExpenses,
        List<CreditorRepayment> repayments,
        double hsdRate, double msIRate, double msIIRate, double cngRate,
        string stationName)
    {
        var dto = new DayReportDto
        {
            StartDate = startDate,
            EndDate = endDate,
            DateString = startDate.Date == endDate.Date ? startDate.ToString("dd/MM/yyyy") : $"{startDate:dd/MM/yyyy} to {endDate:dd/MM/yyyy}",
            StationName = stationName
        };

        // 1. Fuel Sales (Table E)
        double hsdL = 0, hsdA = 0;
        double msIL = 0, msIA = 0;
        double msIIL = 0, msIIA = 0;
        double cngL = 0, cngA = 0;

        var entriesList = MergeConnectedPumpEntries(entries ?? new List<DsmEntry>());
        var todayEntries = entriesList.Where(e => e.Shift != null && e.Shift.ShiftDate.Date >= startDate.Date && e.Shift.ShiftDate.Date <= endDate.Date).ToList();

        (hsdL, hsdA) = _aggregation.GetFuelTotals(todayEntries, "HSD", null);
        (msIL, msIA) = _aggregation.GetFuelTotals(todayEntries, "MS-I", null);
        (msIIL, msIIA) = _aggregation.GetFuelTotals(todayEntries, "MS-II", null);
        (cngL, cngA) = _aggregation.GetFuelTotals(todayEntries, "CNG", null);

        if (hsdA == 0 && hsdL > 0) hsdA = hsdL * hsdRate;
        if (msIIA == 0 && msIIL > 0) msIIA = msIIL * msIIRate;
        if (msIA == 0 && msIL > 0) msIA = msIL * msIRate;
        if (cngA == 0 && cngL > 0) cngA = cngL * cngRate;

        dto.FuelSales = new List<FuelSaleRowDto>();
        if (hsdL > 0 || hsdA > 0)
            dto.FuelSales.Add(new FuelSaleRowDto { Description = "HSD - 20KL", FuelType = "HSD", Litres = hsdL, Rate = hsdL > 0 ? Math.Round(hsdA / hsdL, 2) : hsdRate, Amount = hsdA });
        if (msIIL > 0 || msIIA > 0)
            dto.FuelSales.Add(new FuelSaleRowDto { Description = "HSD - 20KL II", FuelType = "MS-II", Litres = msIIL, Rate = msIIL > 0 ? Math.Round(msIIA / msIIL, 2) : msIIRate, Amount = msIIA });
        if (msIL > 0 || msIA > 0)
            dto.FuelSales.Add(new FuelSaleRowDto { Description = "MS - 20KL", FuelType = "MS-I", Litres = msIL, Rate = msIL > 0 ? Math.Round(msIA / msIL, 2) : msIRate, Amount = msIA });
        if (cngL > 0 || cngA > 0)
            dto.FuelSales.Add(new FuelSaleRowDto { Description = "CNG - Line", FuelType = "CNG", Litres = cngL, Rate = cngL > 0 ? Math.Round(cngA / cngL, 2) : cngRate, Amount = cngA });

        dto.TotalFuelLitres = dto.FuelSales.Sum(f => f.Litres);
        // Use actual nozzle reading amounts to ensure ExpectedCollection matches DSM Summary Gross Sales
        dto.TotalFuelAmount = dto.FuelSales.Sum(f => f.Amount);
        dto.GrandTotalSaleAmount = dto.TotalFuelAmount;

        // 2. DSM Summary (Table A)
        var summaryRows = _aggregation.BuildDsmSummaryRows(todayEntries);
        foreach (var row in summaryRows)
        {
            var entry = todayEntries.FirstOrDefault(e => e.DsmName == row.DsmName && e.PumpId == row.PumpId && e.Shift?.ShiftType == row.Shift);
            if (entry == null) continue;
            var pc = entry.PaymentCollection;

            var rShift = (row.Shift ?? "").Trim().ToUpperInvariant();
            var normShift = (rShift == "I" || rShift == "SHIFT I") ? "A" : (rShift == "II" || rShift == "SHIFT II") ? "B" : (rShift == "III" || rShift == "SHIFT III") ? "C" : rShift;

            if (normShift == "A")
            {
                // Shift I stores both Morning AND Night in the same entry
                row.PhonePeMorning = pc?.PhonePeMorning ?? 0;
                row.PhonePeNight = pc?.PhonePeNight ?? 0;
                row.PhonePeCardMorning = pc?.PhonePeCardMorning ?? 0;
                row.PhonePeCardNight = pc?.PhonePeCardNight ?? 0;
                row.CreditCardMorning = pc?.CreditCardMorning ?? 0;
                row.CreditCardNight = pc?.CreditCardNight ?? 0;
                row.PetroCard = (pc?.PetroCardMorning ?? 0) + (pc?.PetroCardNight ?? 0);
            }
            else if (normShift == "B")
            {
                // Today's Shift II -> Day only (maps to Morning column on-screen)
                row.PhonePeMorning = pc?.PhonePeDay ?? 0;
                row.PhonePeNight = 0;
                row.PhonePeCardMorning = pc?.PhonePeCardDay ?? 0;
                row.PhonePeCardNight = 0;
                row.CreditCardMorning = pc?.CreditCardDay ?? 0;
                row.CreditCardNight = 0;
                row.PetroCard = pc?.PetroCardDay ?? 0;
            }
        }

        dto.DsmSummaryRows = summaryRows;
        dto.DsmSummaryTotals = _aggregation.BuildDsmSummaryTotalRow(summaryRows);

        // 3. Cash Summary (Table B)
        dto.Cash1 = _aggregation.AggregateCash(todayEntries, "Cash1");
        dto.Cash2 = _aggregation.AggregateCash(todayEntries, "Cash2");

        // 4. Debtors (Table C)
        dto.CreditorRows = _aggregation.BuildCreditorRows(todayEntries);
        dto.CreditorsTotal = dto.CreditorRows.Sum(c => c.Amount);

        // 5. Expenses (Table D)
        var expensesList = allExpenses ?? new List<Expense>();
        dto.ExpenseRows = _aggregation.BuildExpenseRows(todayEntries, expensesList);
        dto.ExpensesTotal = dto.ExpenseRows.Where(r => !r.IsShiftLevel).Sum(e => e.Amount);

        // 6. Debtor Repayments
        var repaymentsList = new List<CreditorRepayment>();
        if (repayments != null)
        {
            foreach (var r in repayments)
            {
                var classified = SettlementWindowResolver.Classify(r);
                if (classified.IsValid && classified.BusinessDate >= startDate.Date && classified.BusinessDate <= endDate.Date)
                {
                    repaymentsList.Add(r);
                }
            }
        }
        dto.DebtorRepaymentsTotal = repaymentsList.Sum(r => r.Amount);
        foreach (var r in repaymentsList)
        {
            string mode = r.PaymentMode ?? "";
            if (string.Equals(mode, "Cash", StringComparison.OrdinalIgnoreCase)) dto.CashRepayments += r.Amount;
            else if (string.Equals(mode, "PhonePe", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "PhonePe UPI", StringComparison.OrdinalIgnoreCase)) dto.PhonePeRepayments += r.Amount;
            else if (string.Equals(mode, "Credit Card", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "PineLabs Card", StringComparison.OrdinalIgnoreCase)) dto.CreditCardRepayments += r.Amount;
            else if (string.Equals(mode, "PetroCard", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "Petro Card", StringComparison.OrdinalIgnoreCase)) dto.PetroCardRepayments += r.Amount;
            else if (string.Equals(mode, "Bank Transfer", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "Cheque", StringComparison.OrdinalIgnoreCase)) dto.BankCashRepayments += r.Amount;

            string refNo = "";
            if (r.PaymentMode == "PhonePe" || r.PaymentMode == "Credit Card" || r.PaymentMode == "PineLabs Card" || r.PaymentMode == "PetroCard")
                refNo = $"TID: {r.CardTid}, Batch: {r.CardBatch}";
            else if (r.PaymentMode == "Cheque")
                refNo = $"Chq: {r.ChequeNo}";

            dto.DebtorRepayments.Add(new CreditorRepaymentPrintDto
            {
                DebtorName = r.CreditorName,
                PaymentMode = r.PaymentMode,
                RefNo = refNo,
                Amount = (decimal)r.Amount
            });
        }

        // Build generic RepaymentBreakdown
        dto.RepaymentBreakdown = repaymentsList
            .GroupBy(r => r.PaymentMode ?? "Unknown")
            .Select(g => new RepaymentBreakdownDto
            {
                PaymentMethod = g.Key,
                Amount = g.Sum(x => x.Amount),
                IsReconcilable = IsReconcilableMode(g.Key)
            }).ToList();

        double reconcilableRecoveriesTotal = dto.CashRepayments + dto.PhonePeRepayments
            + dto.CreditCardRepayments + dto.PetroCardRepayments;

        // 7. Oil & DEF Sales (Phase 3/4 Product Sales)
        dto.OilDefSales = new List<OilDefSaleDisplayRow>();
        dto.OilDefSalesTotal = 0;

        // 8. Standard Day-Level Aggregated Digital Collections
        double phonePeDirectMorning = 0;
        double phonePeDirectDay = 0;
        double phonePeDirectNight = 0;
        double phonePeCardMorning = 0;
        double phonePeCardDay = 0;
        double phonePeCardNight = 0;
        double pineLabsCardMorning = 0;
        double pineLabsCardDay = 0;
        double pineLabsCardNight = 0;
        double petroCardMorning = 0;
        double petroCardDay = 0;
        double petroCardNight = 0;

        foreach (var entry in todayEntries)
        {
            var pc = entry.PaymentCollection;
            if (pc == null) continue;

            var rawType = entry.Shift?.ShiftType ?? "";
            var sType = (rawType == "I" || rawType == "Shift I") ? "A" : (rawType == "II" || rawType == "Shift II") ? "B" : (rawType == "III" || rawType == "Shift III") ? "C" : rawType;

            phonePeDirectMorning += pc.PhonePeMorning;
            phonePeCardMorning += pc.PhonePeCardMorning;
            pineLabsCardMorning += pc.CreditCardMorning;
            petroCardMorning += pc.PetroCardMorning;
            phonePeDirectNight += pc.PhonePeNight;
            phonePeCardNight += pc.PhonePeCardNight;
            pineLabsCardNight += pc.CreditCardNight;
            petroCardNight += pc.PetroCardNight;
            phonePeDirectDay += pc.PhonePeDay;
            phonePeCardDay += pc.PhonePeCardDay;
            pineLabsCardDay += pc.CreditCardDay;
            petroCardDay += pc.PetroCardDay;
        }

        // Standardize categories for Day (Aggregates Morning+Day as Morning, and Night as Night)
        double finalPhonePeMorning = phonePeDirectMorning + phonePeDirectDay + dto.PhonePeRepayments;
        double finalPhonePeNight = phonePeDirectNight;
        double finalPhonePeCardMorning = phonePeCardMorning + phonePeCardDay;
        double finalPhonePeCardNight = phonePeCardNight;
        double finalCreditCardMorning = pineLabsCardMorning + pineLabsCardDay + dto.CreditCardRepayments;
        double finalCreditCardNight = pineLabsCardNight;
        double finalPetroCard = petroCardMorning + petroCardDay + petroCardNight + dto.PetroCardRepayments;
        dto.PetroCardMorning = petroCardMorning + petroCardDay + dto.PetroCardRepayments;
        dto.PetroCardNight = petroCardNight;

        // 9. Testing summary totals
        double msTesting = 0, msTestingVol = 0;
        double hsdTesting = 0, hsdTestingVol = 0;
        double hsdTesting2 = 0, hsdTesting2Vol = 0;
        double cngTesting = 0, cngTestingVol = 0;

        foreach (var entry in todayEntries)
        {
            foreach (var t in entry.TestingEntries)
            {
                var cat = PumpConfiguration.GetTestingTankCategory(t.FuelType, entry.PumpId, startDate.Date);
                double tAmt = t.Amount > 0 ? (double)t.Amount : (double)(t.Litres * t.Rate);
                double tVol = (double)t.Litres;
                if (cat == "MS") { msTesting += tAmt; msTestingVol += tVol; }
                else if (cat == "HSD") { hsdTesting += tAmt; hsdTestingVol += tVol; }
                else if (cat == "HSD-II") { hsdTesting2 += tAmt; hsdTesting2Vol += tVol; }
                else if (cat == "CNG") { cngTesting += tAmt; cngTestingVol += tVol; }
            }
        }
        double testingTotal = msTesting + hsdTesting + hsdTesting2 + cngTesting;

        // 10. DSM Short calculation
        double totalDsmShort = CalculateDsmShort(todayEntries);
        dto.TotalDsmShort = totalDsmShort;

        // 11. Oil & DEF Sales
        var oilDefResult = ExtractOilDefSales(todayEntries);
        dto.OilDefSales = oilDefResult.Rows;
        dto.OilDefSalesTotal = oilDefResult.Total;

        // 12. Build standardized collection categories with audit breakdown (Oil & DEF sales excluded from Final Reconciliation)
        dto.CollectionBreakdown = new List<CollectionCategoryDto>
        {
            new() { Category = "Cash Deposit", Amount = dto.Cash1.GrandTotal, BaseAmount = dto.Cash1.GrandTotal, RecoveryAmount = 0 },
            new() { Category = "Cash In Hand", Amount = dto.Cash2.GrandTotal + dto.CashRepayments, BaseAmount = dto.Cash2.GrandTotal, RecoveryAmount = dto.CashRepayments },
            new() { Category = "PhonePe Morning", Amount = finalPhonePeMorning, BaseAmount = phonePeDirectMorning + phonePeDirectDay, RecoveryAmount = dto.PhonePeRepayments },
            new() { Category = "PhonePe Night", Amount = finalPhonePeNight, BaseAmount = phonePeDirectNight, RecoveryAmount = 0 },
            new() { Category = "Card Morning", Amount = finalCreditCardMorning, BaseAmount = pineLabsCardMorning + pineLabsCardDay, RecoveryAmount = dto.CreditCardRepayments },
            new() { Category = "Card Night", Amount = finalCreditCardNight, BaseAmount = pineLabsCardNight, RecoveryAmount = 0 },
            new() { Category = "Petro Card Morning", Amount = dto.PetroCardMorning, BaseAmount = petroCardMorning + petroCardDay, RecoveryAmount = dto.PetroCardRepayments },
            new() { Category = "Petro Card Night", Amount = dto.PetroCardNight, BaseAmount = petroCardNight, RecoveryAmount = 0 },
            new() { Category = "Debtors", Amount = dto.CreditorsTotal, BaseAmount = dto.CreditorsTotal, RecoveryAmount = 0 },
            new() { Category = "Expenses", Amount = dto.ExpensesTotal, BaseAmount = dto.ExpensesTotal, RecoveryAmount = 0 },
            new() { Category = "MS Testing", Amount = msTesting, BaseAmount = msTesting, RecoveryAmount = 0, Volume = msTestingVol },
            new() { Category = "HSD Testing I", Amount = hsdTesting, BaseAmount = hsdTesting, RecoveryAmount = 0, Volume = hsdTestingVol },
            new() { Category = "HSD Testing II", Amount = hsdTesting2, BaseAmount = hsdTesting2, RecoveryAmount = 0, Volume = hsdTesting2Vol },
            new() { Category = "DSM Short", Amount = totalDsmShort, BaseAmount = totalDsmShort, RecoveryAmount = 0 }
        };

        double khandhareTotal = todayEntries.SelectMany(e => e.KhandharePetroleumEntries ?? new List<KhandharePetroleumEntry>()).Sum(k => k.Amount);
        if (khandhareTotal > 0)
        {
            dto.CollectionBreakdown.Add(new() { Category = "Kandhare Petroleum", Amount = khandhareTotal, BaseAmount = khandhareTotal, RecoveryAmount = 0 });
        }

        // 12. Final Reconciliation
        dto.ActualCollection = dto.CollectionBreakdown.Where(c => c.Category != "DSM Short").Sum(c => c.Amount);
        dto.ExpectedCollection = dto.TotalFuelAmount + dto.OilDefSalesTotal + reconcilableRecoveriesTotal;
        dto.Difference = dto.ActualCollection - dto.ExpectedCollection;
        dto.IsBalanced = Math.Abs(dto.Difference) < 0.01;
        dto.BalancedStatus = dto.IsBalanced ? "Balanced" : (dto.Difference < 0 ? "Short" : "Excess");

        dto.PersonalDebtors = todayEntries
            .SelectMany(e => e.PersonalDebtors ?? new List<DsmPersonalDebtor>())
            .Select(pd => new DsmPersonalDebtorPrintDto
            {
                DsmName = pd.DsmName,
                FuelProduct = pd.FuelProduct ?? string.Empty,
                Remarks = pd.Remarks ?? string.Empty,
                Amount = pd.Amount
            })
            .ToList();

        dto.KhandhareEntries = todayEntries
            .SelectMany(e => e.KhandharePetroleumEntries ?? new List<KhandharePetroleumEntry>())
            .Select(kp => new KhandharePetroleumPrintDto
            {
                Name = kp.Name,
                VehicleNumber = kp.VehicleNumber ?? string.Empty,
                SlipNumber = kp.SlipNumber,
                Amount = kp.Amount
            })
            .ToList();

        dto.PersonalDebtorRepayments = ExtractPersonalDebtorRepayments(todayEntries, startDate);

        return dto;
    }

    private double CalculateDsmShort(List<DsmEntry> entries)
    {
        double totalDsmShort = 0;
        var merged = MergeConnectedPumpEntries(entries);
        foreach (var e in merged)
        {
            double gs = (double)e.GrossSales;
            double totalCollection = (double)e.TotalCollection;
            double mismatch = totalCollection - gs;

            if (mismatch < -0.01)
            {
                totalDsmShort += Math.Abs(mismatch);
            }
        }
        return totalDsmShort;
    }

    private static bool IsReconcilableMode(string mode)
    {
        if (string.IsNullOrEmpty(mode)) return false;
        return string.Equals(mode, "Cash", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "PhonePe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "PhonePe UPI", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "UPI Terminal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "Credit Card", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "PineLabs Card", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "PineLabs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "PetroCard", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "Petro Card", StringComparison.OrdinalIgnoreCase);
    }

    private static List<DsmEntry> MergeConnectedPumpEntries(List<DsmEntry> entries)
    {
        if (entries == null || entries.Count == 0) return new List<DsmEntry>();

        var primaryEntries = entries.Where(e => !e.ReconciledToPumpId.HasValue).OrderBy(e => e.DsmEntryId).ToList();
        var slaveEntries = entries.Where(e => e.ReconciledToPumpId.HasValue).ToList();
        var usedSlaveIds = new HashSet<int>();
        var mergedEntries = new List<DsmEntry>();

        foreach (var primary in primaryEntries)
        {
            var slave = slaveEntries
                .Where(e => !usedSlaveIds.Contains(e.DsmEntryId)
                    && (e.ReconciledToPumpId == primary.DsmEntryId || (e.ReconciledToPumpId == primary.PumpId && string.Equals((e.DsmName ?? "").Trim(), (primary.DsmName ?? "").Trim(), StringComparison.OrdinalIgnoreCase))))
                .OrderBy(e => e.ReconciledToPumpId == primary.DsmEntryId ? 0 : 1)
                .ThenBy(e => e.DsmEntryId >= primary.DsmEntryId ? (e.DsmEntryId - primary.DsmEntryId) : (100000 + Math.Abs(e.DsmEntryId - primary.DsmEntryId)))
                .FirstOrDefault();

            if (slave != null)
            {
                usedSlaveIds.Add(slave.DsmEntryId);
            }

            var group = slave != null ? new List<DsmEntry> { primary, slave } : new List<DsmEntry> { primary };

            decimal grossSales = primary.GrossSales > 0 
                ? primary.GrossSales 
                : (decimal)group.SelectMany(e => e.NozzleReadings ?? new List<NozzleReading>()).Sum(n => n.Amount);

            var merged = new DsmEntry
            {
                DsmEntryId = primary.DsmEntryId,
                ShiftId = primary.ShiftId,
                Shift = primary.Shift,
                DsmName = primary.DsmName,
                PumpId = primary.PumpId,
                ConnectedPumpId = primary.ConnectedPumpId ?? slave?.PumpId,
                ReconciledToPumpId = null,
                StartTime = primary.StartTime,
                EndTime = primary.EndTime,
                GrossSales = grossSales,
                TotalInDirect = primary.TotalInDirect,
                TotalCreditors = primary.TotalCreditors,
                TotalCollection = primary.TotalCollection,
                Mismatch = primary.TotalCollection - grossSales,
                CreatedAt = primary.CreatedAt,
                UpdatedAt = primary.UpdatedAt
            };

            // Merge child collections for THIS specific submission group only
            merged.NozzleReadings = group.SelectMany(e => e.NozzleReadings ?? new List<NozzleReading>()).ToList();
            merged.CashDenominations = primary.CashDenominations ?? new List<CashDenomination>();
            merged.DebitEntries = group.SelectMany(e => e.DebitEntries ?? new List<DebitEntry>()).ToList();
            merged.Expenses = group.SelectMany(e => e.Expenses ?? new List<Expense>()).ToList();
            merged.TestingEntries = group.SelectMany(e => e.TestingEntries ?? new List<TestingEntry>()).ToList();
            merged.PersonalDebtors = group.SelectMany(e => e.PersonalDebtors ?? new List<DsmPersonalDebtor>()).ToList();
            merged.KhandharePetroleumEntries = group.SelectMany(e => e.KhandharePetroleumEntries ?? new List<KhandharePetroleumEntry>()).ToList();
            merged.PaymentCollection = primary.PaymentCollection;

            mergedEntries.Add(merged);
        }

        foreach (var slave in slaveEntries.Where(e => !usedSlaveIds.Contains(e.DsmEntryId)))
        {
            mergedEntries.Add(slave);
        }

        return mergedEntries;
    }

    public static (List<OilDefSaleDisplayRow> Rows, double Total) ExtractOilDefSales(IEnumerable<DsmEntry> entries)
    {
        var list = new List<OilDefSaleDisplayRow>();
        if (entries == null) return (list, 0);

        foreach (var entry in entries)
        {
            string? metaJson = null;
            var prop = entry.GetType().GetProperty("MetadataJson");
            if (prop != null)
            {
                metaJson = prop.GetValue(entry) as string;
            }

            if (string.IsNullOrWhiteSpace(metaJson)) continue;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(metaJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("oilDefSales", out var salesElement) && salesElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var sale in salesElement.EnumerateArray())
                    {
                        string productName = sale.TryGetProperty("productName", out var pn) ? pn.GetString() ?? "" : "";
                        string category = sale.TryGetProperty("category", out var cat) ? cat.GetString() ?? "Oil" : "Oil";

                        double qty = 0;
                        if (sale.TryGetProperty("quantity", out var q))
                        {
                            if (q.ValueKind == System.Text.Json.JsonValueKind.Number) q.TryGetDouble(out qty);
                            else if (q.ValueKind == System.Text.Json.JsonValueKind.String) double.TryParse(q.GetString(), out qty);
                        }

                        double price = 0;
                        if (sale.TryGetProperty("price", out var pr))
                        {
                            if (pr.ValueKind == System.Text.Json.JsonValueKind.Number) pr.TryGetDouble(out price);
                            else if (pr.ValueKind == System.Text.Json.JsonValueKind.String) double.TryParse(pr.GetString(), out price);
                        }
                        else if (sale.TryGetProperty("rate", out var r))
                        {
                            if (r.ValueKind == System.Text.Json.JsonValueKind.Number) r.TryGetDouble(out price);
                            else if (r.ValueKind == System.Text.Json.JsonValueKind.String) double.TryParse(r.GetString(), out price);
                        }

                        double total = 0;
                        if (sale.TryGetProperty("total", out var tot))
                        {
                            if (tot.ValueKind == System.Text.Json.JsonValueKind.Number) tot.TryGetDouble(out total);
                            else if (tot.ValueKind == System.Text.Json.JsonValueKind.String) double.TryParse(tot.GetString(), out total);
                        }
                        if (total == 0 && qty > 0 && price > 0)
                        {
                            total = qty * price;
                        }

                        if (qty > 0 || total > 0 || !string.IsNullOrWhiteSpace(productName))
                        {
                            list.Add(new OilDefSaleDisplayRow
                            {
                                ProductName = string.IsNullOrWhiteSpace(productName) ? category : productName,
                                Category = category,
                                Quantity = qty,
                                Rate = price,
                                Total = total
                            });
                        }
                    }
                }
            }
            catch
            {
                // Ignore parsing errors for invalid MetadataJson
            }
        }

        return (list, list.Sum(s => s.Total));
    }

    private static List<DsmPersonalDebtorRepaymentPrintDto> ExtractPersonalDebtorRepayments(IEnumerable<DsmEntry> entries, DateTime date)
    {
        var list = new List<DsmPersonalDebtorRepaymentPrintDto>();
        try
        {
            var spProp = Type.GetType("FuelPro.UI.App, FuelPro.UI")?.GetProperty("Services");
            var sp = spProp?.GetValue(null) as IServiceProvider;
            if (sp != null)
            {
                var dbType = Type.GetType("FuelPro.Data.FuelProDbContext, FuelPro.Data");
                if (dbType != null)
                {
                    var db = sp.GetService(dbType) as Microsoft.EntityFrameworkCore.DbContext;
                    if (db != null)
                    {
                        var set = db.Set<DsmPersonalDebtorRepayment>();
                        var startDate = date.Date;
                        var endDate = startDate.AddDays(1);
                        var repayments = set.Include(r => r.DsmPersonalDebtor)
                            .Where(r => r.Date >= startDate && r.Date < endDate)
                            .ToList();
                        foreach (var r in repayments)
                        {
                            list.Add(new DsmPersonalDebtorRepaymentPrintDto
                            {
                                DsmName = r.DsmPersonalDebtor?.DsmName ?? "DSM",
                                PaymentMethod = r.PaymentMethod ?? "Cash",
                                RefNo = !string.IsNullOrWhiteSpace(r.CardTid) ? $"TID: {r.CardTid}" : (r.PaymentMethod ?? "Cash"),
                                Amount = r.Amount
                            });
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore if missing
        }
        return list;
    }
}
