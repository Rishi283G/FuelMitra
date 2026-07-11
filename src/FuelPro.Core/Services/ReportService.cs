using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

        dto.FuelSales = new List<FuelSaleRowDto>();
        if (hsdL > 0 || hsdA > 0)
            dto.FuelSales.Add(new FuelSaleRowDto { Description = "HSD - 20KL", FuelType = "HSD", Litres = hsdL, Rate = hsdRate, Amount = hsdL * hsdRate });
        if (msIIL > 0 || msIIA > 0)
            dto.FuelSales.Add(new FuelSaleRowDto { Description = "HSD - 20KL II", FuelType = "MS-II", Litres = msIIL, Rate = msIIRate, Amount = msIIL * msIIRate });
        if (msIL > 0 || msIA > 0)
            dto.FuelSales.Add(new FuelSaleRowDto { Description = "MS - 20KL", FuelType = "MS-I", Litres = msIL, Rate = msIRate, Amount = msIL * msIRate });
        if (cngL > 0 || cngA > 0)
            dto.FuelSales.Add(new FuelSaleRowDto { Description = "CNG - Line", FuelType = "CNG", Litres = cngL, Rate = cngRate, Amount = cngL * cngRate });

        dto.TotalFuelLitres = dto.FuelSales.Sum(f => f.Litres);
        dto.TotalFuelAmount = entriesList.Sum(e => e.GrossSales > 0 ? (double)e.GrossSales : e.NozzleReadings.Sum(n => n.Amount));
        dto.OtherCashTotal = otherCashList != null ? otherCashList.Sum(o => o.Amount) : 0;
        dto.GrandTotalSaleAmount = dto.TotalFuelAmount; // Adjusts with other cash in reconciliation if needed

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
        var repaymentsList = repayments ?? new List<CreditorRepayment>();
        dto.DebtorRepaymentsTotal = repaymentsList.Sum(r => r.Amount);
        foreach (var r in repaymentsList)
        {
            string mode = r.PaymentMode ?? "";
            if (string.Equals(mode, "Cash", StringComparison.OrdinalIgnoreCase)) dto.CashRepayments += r.Amount;
            else if (string.Equals(mode, "PhonePe", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "PhonePe UPI", StringComparison.OrdinalIgnoreCase)) dto.PhonePeRepayments += r.Amount;
            else if (string.Equals(mode, "Credit Card", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "PineLabs Card", StringComparison.OrdinalIgnoreCase)) dto.CreditCardRepayments += r.Amount;
            else if (string.Equals(mode, "PetroCard", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "Petro Card", StringComparison.OrdinalIgnoreCase)) dto.PetroCardRepayments += r.Amount;
            else if (string.Equals(mode, "Bank Transfer", StringComparison.OrdinalIgnoreCase)) dto.BankCashRepayments += r.Amount;

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

        // 7. Oil & DEF Sales (Phase 3/4 Product Sales)
        dto.OilDefSales = new List<OilDefSaleDisplayRow>();
        dto.OilDefSalesTotal = 0;

        // 8. TID Card Data Calculations
        double phonePeCardMorning = 0;
        double phonePeCardNight = 0;
        double phonePeMorning = 0;
        double phonePeNight = 0;
        double petroCard = 0;
        double creditCardMorning = 0;
        double creditCardNight = 0;

        if (todayTid != null && tomorrowTid != null)
        {
            if (shiftType == "B")
            {
                phonePeCardMorning = todayTid.PhonePeCardDay;
                phonePeMorning = todayTid.PhonePeDirectDay;
                petroCard = todayTid.PetroCardDay;
                creditCardMorning = todayTid.PineLabsCardDay;
            }
            else
            {
                phonePeCardMorning = tomorrowTid.PhonePeCardMorning;
                phonePeCardNight = todayTid.PhonePeCardNight;
                phonePeMorning = tomorrowTid.PhonePeDirectMorning;
                phonePeNight = todayTid.PhonePeDirectNight;
                petroCard = tomorrowTid.PetroCardMorning + todayTid.PetroCardNight;
                creditCardMorning = tomorrowTid.PineLabsCardMorning;
                creditCardNight = todayTid.PineLabsCardNight;
            }
        }

        // Apply debtor repayments adjustments (silent additions per logic rules)
        double finalCashDeposit = dto.Cash1.GrandTotal + dto.BankCashRepayments;
        double finalCashInHand = dto.Cash2.GrandTotal + dto.CashRepayments;
        double finalPhonePeMorning = phonePeMorning + (shiftType == "B" ? dto.PhonePeRepayments : 0);
        double finalPhonePeNight = phonePeNight + (shiftType != "B" ? dto.PhonePeRepayments : 0);
        double finalCreditCardMorning = creditCardMorning + (shiftType == "B" ? dto.CreditCardRepayments : 0);
        double finalCreditCardNight = creditCardNight + (shiftType != "B" ? dto.CreditCardRepayments : 0);
        double finalPetroCard = petroCard + dto.PetroCardRepayments;

        // 9. Testing summary totals
        double msTesting = 0;
        double hsdTesting = 0;
        double hsdTesting2 = 0;
        double cngTesting = 0;

        foreach (var entry in entriesList)
        {
            foreach (var t in entry.TestingEntries)
            {
                var cat = PumpConfiguration.GetTestingTankCategory(t.FuelType, entry.PumpId, date.Date);
                if (cat == "MS") msTesting += (double)t.Amount;
                else if (cat == "HSD") hsdTesting += (double)t.Amount;
                else if (cat == "HSD-II") hsdTesting2 += (double)t.Amount;
                else if (cat == "CNG") cngTesting += (double)t.Amount;
            }
        }
        double testingTotal = msTesting + hsdTesting + hsdTesting2 + cngTesting;

        // 10. DSM Short calculation
        double totalDsmShort = CalculateDsmShort(entriesList);
        dto.TotalDsmShort = totalDsmShort;

        // 11. Build standardized collection categories (16 items)
        dto.CollectionBreakdown = new List<CollectionCategoryDto>
        {
            new() { Category = "Cash Deposit", Amount = finalCashDeposit },
            new() { Category = "Cash In Hand", Amount = finalCashInHand },
            new() { Category = "PhonePe Morning", Amount = finalPhonePeMorning },
            new() { Category = "PhonePe Night", Amount = finalPhonePeNight },
            new() { Category = "PhonePe Card Morning", Amount = phonePeCardMorning },
            new() { Category = "PhonePe Card Night", Amount = phonePeCardNight },
            new() { Category = "PineLabs Morning", Amount = finalCreditCardMorning },
            new() { Category = "PineLabs Night", Amount = finalCreditCardNight },
            new() { Category = "Petro Card", Amount = finalPetroCard },
            new() { Category = "Debtors", Amount = dto.CreditorsTotal },
            new() { Category = "Other Collections", Amount = dto.OtherCashTotal },
            new() { Category = "Oil Sales", Amount = 0 },
            new() { Category = "DEF Sales", Amount = 0 },
            new() { Category = "Expenses", Amount = dto.ExpensesTotal },
            new() { Category = "Testing", Amount = testingTotal },
            new() { Category = "DSM Short", Amount = totalDsmShort }
        };

        // 12. Final Reconciliation
        dto.ActualCollection = dto.CollectionBreakdown.Sum(c => c.Amount);
        dto.ExpectedCollection = dto.TotalFuelAmount + dto.OilDefSalesTotal + dto.OtherCashTotal + dto.DebtorRepaymentsTotal;
        dto.Difference = dto.ActualCollection - dto.ExpectedCollection;
        dto.IsBalanced = Math.Abs(dto.Difference) < 0.01;
        dto.BalancedStatus = dto.IsBalanced ? "Balanced" : (dto.Difference < 0 ? "Short" : "Excess");

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
        (hsdL, hsdA) = _aggregation.GetFuelTotals(entriesList, "HSD", null);
        (msIL, msIA) = _aggregation.GetFuelTotals(entriesList, "MS-I", null);
        (msIIL, msIIA) = _aggregation.GetFuelTotals(entriesList, "MS-II", null);
        (cngL, cngA) = _aggregation.GetFuelTotals(entriesList, "CNG", null);

        dto.FuelSales = new List<FuelSaleRowDto>();
        if (hsdL > 0 || hsdA > 0)
            dto.FuelSales.Add(new FuelSaleRowDto { Description = "HSD - 20KL", FuelType = "HSD", Litres = hsdL, Rate = hsdRate, Amount = hsdL * hsdRate });
        if (msIIL > 0 || msIIA > 0)
            dto.FuelSales.Add(new FuelSaleRowDto { Description = "HSD - 20KL II", FuelType = "MS-II", Litres = msIIL, Rate = msIIRate, Amount = msIIL * msIIRate });
        if (msIL > 0 || msIA > 0)
            dto.FuelSales.Add(new FuelSaleRowDto { Description = "MS - 20KL", FuelType = "MS-I", Litres = msIL, Rate = msIRate, Amount = msIL * msIRate });
        if (cngL > 0 || cngA > 0)
            dto.FuelSales.Add(new FuelSaleRowDto { Description = "CNG - Line", FuelType = "CNG", Litres = cngL, Rate = cngRate, Amount = cngL * cngRate });

        dto.TotalFuelLitres = dto.FuelSales.Sum(f => f.Litres);
        dto.TotalFuelAmount = entriesList.Sum(e => e.GrossSales > 0 ? (double)e.GrossSales : e.NozzleReadings.Sum(n => n.Amount));
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
        var expensesList = allExpenses ?? new List<Expense>();
        dto.ExpenseRows = _aggregation.BuildExpenseRows(entriesList, expensesList);
        dto.ExpensesTotal = dto.ExpenseRows.Sum(e => e.Amount);

        // 6. Debtor Repayments
        var repaymentsList = repayments ?? new List<CreditorRepayment>();
        dto.DebtorRepaymentsTotal = repaymentsList.Sum(r => r.Amount);
        foreach (var r in repaymentsList)
        {
            string mode = r.PaymentMode ?? "";
            if (string.Equals(mode, "Cash", StringComparison.OrdinalIgnoreCase)) dto.CashRepayments += r.Amount;
            else if (string.Equals(mode, "PhonePe", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "PhonePe UPI", StringComparison.OrdinalIgnoreCase)) dto.PhonePeRepayments += r.Amount;
            else if (string.Equals(mode, "Credit Card", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "PineLabs Card", StringComparison.OrdinalIgnoreCase)) dto.CreditCardRepayments += r.Amount;
            else if (string.Equals(mode, "PetroCard", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "Petro Card", StringComparison.OrdinalIgnoreCase)) dto.PetroCardRepayments += r.Amount;
            else if (string.Equals(mode, "Bank Transfer", StringComparison.OrdinalIgnoreCase)) dto.BankCashRepayments += r.Amount;

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

        foreach (var entry in entriesList)
        {
            var pc = entry.PaymentCollection;
            if (pc == null) continue;

            phonePeDirectMorning += pc.PhonePeMorning;
            phonePeDirectDay += pc.PhonePeDay;
            phonePeDirectNight += pc.PhonePeNight;

            phonePeCardMorning += pc.PhonePeCardMorning;
            phonePeCardDay += pc.PhonePeCardDay;
            phonePeCardNight += pc.PhonePeCardNight;

            pineLabsCardMorning += pc.CreditCardMorning;
            pineLabsCardDay += pc.CreditCardDay;
            pineLabsCardNight += pc.CreditCardNight;

            petroCardMorning += pc.PetroCardMorning;
            petroCardDay += pc.PetroCardDay;
            petroCardNight += pc.PetroCardNight;
        }

        // Standardize categories for Day (Aggregates Morning+Day as Morning, and Night as Night)
        double finalPhonePeMorning = phonePeDirectMorning + phonePeDirectDay + dto.PhonePeRepayments;
        double finalPhonePeNight = phonePeDirectNight;
        double finalPhonePeCardMorning = phonePeCardMorning + phonePeCardDay;
        double finalPhonePeCardNight = phonePeCardNight;
        double finalCreditCardMorning = pineLabsCardMorning + pineLabsCardDay + dto.CreditCardRepayments;
        double finalCreditCardNight = pineLabsCardNight;
        double finalPetroCard = petroCardMorning + petroCardDay + petroCardNight + dto.PetroCardRepayments;

        // 9. Testing summary totals
        double msTesting = 0;
        double hsdTesting = 0;
        double hsdTesting2 = 0;
        double cngTesting = 0;

        foreach (var entry in entriesList)
        {
            foreach (var t in entry.TestingEntries)
            {
                var cat = PumpConfiguration.GetTestingTankCategory(t.FuelType, entry.PumpId, startDate.Date);
                if (cat == "MS") msTesting += (double)t.Amount;
                else if (cat == "HSD") hsdTesting += (double)t.Amount;
                else if (cat == "HSD-II") hsdTesting2 += (double)t.Amount;
                else if (cat == "CNG") cngTesting += (double)t.Amount;
            }
        }
        double testingTotal = msTesting + hsdTesting + hsdTesting2 + cngTesting;

        // 10. DSM Short calculation
        double totalDsmShort = CalculateDsmShort(entriesList);
        dto.TotalDsmShort = totalDsmShort;

        // 11. Build standardized collection categories (16 items)
        dto.CollectionBreakdown = new List<CollectionCategoryDto>
        {
            new() { Category = "Cash Deposit", Amount = dto.Cash1.GrandTotal + dto.BankCashRepayments },
            new() { Category = "Cash In Hand", Amount = dto.Cash2.GrandTotal + dto.CashRepayments },
            new() { Category = "PhonePe Morning", Amount = finalPhonePeMorning },
            new() { Category = "PhonePe Night", Amount = finalPhonePeNight },
            new() { Category = "PhonePe Card Morning", Amount = finalPhonePeCardMorning },
            new() { Category = "PhonePe Card Night", Amount = finalPhonePeCardNight },
            new() { Category = "PineLabs Morning", Amount = finalCreditCardMorning },
            new() { Category = "PineLabs Night", Amount = finalCreditCardNight },
            new() { Category = "Petro Card", Amount = finalPetroCard },
            new() { Category = "Debtors", Amount = dto.CreditorsTotal },
            new() { Category = "Other Collections", Amount = dto.OtherCashTotal },
            new() { Category = "Oil Sales", Amount = 0 },
            new() { Category = "DEF Sales", Amount = 0 },
            new() { Category = "Expenses", Amount = dto.ExpensesTotal },
            new() { Category = "Testing", Amount = testingTotal },
            new() { Category = "DSM Short", Amount = totalDsmShort }
        };

        // 12. Final Reconciliation
        dto.ActualCollection = dto.CollectionBreakdown.Sum(c => c.Amount);
        dto.ExpectedCollection = dto.TotalFuelAmount + dto.OilDefSalesTotal + dto.OtherCashTotal + dto.DebtorRepaymentsTotal;
        dto.Difference = dto.ActualCollection - dto.ExpectedCollection;
        dto.IsBalanced = Math.Abs(dto.Difference) < 0.01;
        dto.BalancedStatus = dto.IsBalanced ? "Balanced" : (dto.Difference < 0 ? "Short" : "Excess");

        return dto;
    }

    private double CalculateDsmShort(List<DsmEntry> entries)
    {
        double totalDsmShort = 0;
        var mismatchGroups = entries.GroupBy(e => new { e.ShiftId, e.DsmName, GroupPumpId = e.ReconciledToPumpId ?? e.PumpId });
        foreach (var g in mismatchGroups)
        {
            var gs = g.SelectMany(e => e.NozzleReadings).Sum(n => n.Amount);
            var cash1Total = g.SelectMany(e => e.CashDenominations).Where(c => c.CashType == "Cash1").Sum(c => c.TotalAmount);
            var cash2Total = g.SelectMany(e => e.CashDenominations).Where(c => c.CashType == "Cash2").Sum(c => c.TotalAmount);
            
            var pp = g.Sum(e => (e.PaymentCollection?.PhonePeMorning ?? 0) 
                                   + (e.PaymentCollection?.PhonePeDay ?? 0)
                                   + (e.PaymentCollection?.PhonePeNight ?? 0) 
                                   + (e.PaymentCollection?.PhonePeCardMorning ?? 0) 
                                   + (e.PaymentCollection?.PhonePeCardDay ?? 0)
                                   + (e.PaymentCollection?.PhonePeCardNight ?? 0));
                                   
            var cc = g.Sum(e => (e.PaymentCollection?.CreditCardMorning ?? 0) 
                                       + (e.PaymentCollection?.CreditCardDay ?? 0)
                                       + (e.PaymentCollection?.CreditCardNight ?? 0) 
                                       + (e.PaymentCollection?.PetroCardMorning ?? 0) 
                                       + (e.PaymentCollection?.PetroCardDay ?? 0)
                                       + (e.PaymentCollection?.PetroCardNight ?? 0));
                                       
            var cashDepositVal = cash1Total > 0 ? cash1Total : g.Sum(e => e.PaymentCollection?.CashDeposit ?? 0);
            var totalInDirect = (double)(pp + cc + cashDepositVal + cash2Total);
            
            var totalCreditors = g.SelectMany(e => e.DebitEntries).Sum(d => d.Amount);
            var dsmTesting = g.SelectMany(e => e.TestingEntries).Sum(t => t.Amount);
            var totalExpenses = g.SelectMany(e => e.Expenses).Sum(ex => ex.Amount);
            
            var totalCollection = totalInDirect + totalCreditors + dsmTesting + totalExpenses;
            var mismatch = totalCollection - gs;
            
            if (mismatch < -0.01)
            {
                totalDsmShort += Math.Abs(mismatch);
            }
        }
        return totalDsmShort;
    }

    private static List<DsmEntry> MergeConnectedPumpEntries(List<DsmEntry> entries)
    {
        if (entries == null || entries.Count == 0) return new List<DsmEntry>();

        var mergedEntries = new List<DsmEntry>();
        // Group entries by ShiftId, DsmName (case-insensitive), and the Effective Primary Pump ID.
        // Effective Primary Pump ID is ReconciledToPumpId if set, otherwise PumpId.
        var groups = entries.GroupBy(e => new { 
            e.ShiftId, 
            DsmName = (e.DsmName ?? "").Trim().ToLower(), 
            PrimaryPumpId = e.ReconciledToPumpId ?? e.PumpId 
        });

        foreach (var group in groups)
        {
            // The primary entry is the one that has ReconciledToPumpId == null
            var primary = group.FirstOrDefault(e => e.ReconciledToPumpId == null) ?? group.First();
            
            // Create a new merged DsmEntry to avoid mutating EF tracked instances
            var merged = new DsmEntry
            {
                DsmEntryId = primary.DsmEntryId,
                ShiftId = primary.ShiftId,
                Shift = primary.Shift,
                DsmName = primary.DsmName,
                PumpId = primary.PumpId, // Main primary pump
                ConnectedPumpId = primary.ConnectedPumpId,
                ReconciledToPumpId = null,
                StartTime = primary.StartTime,
                EndTime = primary.EndTime,
                GrossSales = group.Sum(e => e.GrossSales),
                TotalInDirect = group.Sum(e => e.TotalInDirect),
                TotalCreditors = group.Sum(e => e.TotalCreditors),
                TotalCollection = group.Sum(e => e.TotalCollection),
                Mismatch = group.Sum(e => e.Mismatch)
            };

            // Merge child collections
            merged.NozzleReadings = group.SelectMany(e => e.NozzleReadings ?? new List<NozzleReading>()).ToList();
            merged.CashDenominations = group.SelectMany(e => e.CashDenominations ?? new List<CashDenomination>()).ToList();
            merged.DebitEntries = group.SelectMany(e => e.DebitEntries ?? new List<DebitEntry>()).ToList();
            merged.Expenses = group.SelectMany(e => e.Expenses ?? new List<Expense>()).ToList();
            merged.TestingEntries = group.SelectMany(e => e.TestingEntries ?? new List<TestingEntry>()).ToList();
            
            // Merge PaymentCollection
            var mainPayment = group.FirstOrDefault(e => e.PaymentCollection != null)?.PaymentCollection;
            if (mainPayment != null)
            {
                merged.PaymentCollection = new PaymentCollection
                {
                    PaymentId = mainPayment.PaymentId,
                    DsmEntryId = merged.DsmEntryId,
                    PhonePeMorning = group.Sum(e => e.PaymentCollection?.PhonePeMorning ?? 0),
                    PhonePeDay = group.Sum(e => e.PaymentCollection?.PhonePeDay ?? 0),
                    PhonePeNight = group.Sum(e => e.PaymentCollection?.PhonePeNight ?? 0),
                    PhonePeCardMorning = group.Sum(e => e.PaymentCollection?.PhonePeCardMorning ?? 0),
                    PhonePeCardDay = group.Sum(e => e.PaymentCollection?.PhonePeCardDay ?? 0),
                    PhonePeCardNight = group.Sum(e => e.PaymentCollection?.PhonePeCardNight ?? 0),
                    CreditCardMorning = group.Sum(e => e.PaymentCollection?.CreditCardMorning ?? 0),
                    CreditCardDay = group.Sum(e => e.PaymentCollection?.CreditCardDay ?? 0),
                    CreditCardNight = group.Sum(e => e.PaymentCollection?.CreditCardNight ?? 0),
                    PetroCardMorning = group.Sum(e => e.PaymentCollection?.PetroCardMorning ?? 0),
                    PetroCardDay = group.Sum(e => e.PaymentCollection?.PetroCardDay ?? 0),
                    PetroCardNight = group.Sum(e => e.PaymentCollection?.PetroCardNight ?? 0),
                    CashDeposit = group.Sum(e => e.PaymentCollection?.CashDeposit ?? 0),
                    Others = group.Sum(e => e.PaymentCollection?.Others ?? 0)
                };
            }

            mergedEntries.Add(merged);
        }

        return mergedEntries;
    }
}
