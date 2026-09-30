using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using Serilog;

namespace FuelPro.Core.Services;

public class ReportService : IReportService
{
    private readonly IShiftAggregationService _aggregation;
    private readonly IServiceProvider? _serviceProvider;
    private readonly ILogger _logger = Log.ForContext<ReportService>();

    public ReportService(IShiftAggregationService aggregation, IServiceProvider? serviceProvider = null)
    {
        _aggregation = aggregation;
        _serviceProvider = serviceProvider;
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

        // 1. Fuel Sales (Table E / DSR)
        var entriesList = MergeConnectedPumpEntries(entries ?? new List<DsmEntry>());
        dto.FuelSales = BuildDynamicFuelSales(entriesList, hsdRate, msIRate, msIIRate, cngRate, date);
        dto.TotalFuelLitres = dto.FuelSales.Sum(f => f.Litres);
        dto.TotalFuelAmount = dto.FuelSales.Sum(f => f.Amount);
        dto.OtherCashTotal = otherCashList != null ? otherCashList.Sum(o => o.Amount) : 0;
        dto.GrandTotalSaleAmount = dto.TotalFuelAmount;

        // 2. DSM Summary (Table A)
        var summaryRows = _aggregation.BuildDsmSummaryRows(entriesList);
        dto.DsmSummaryRows = summaryRows;
        dto.DsmSummaryTotals = _aggregation.BuildDsmSummaryTotalRow(summaryRows);
        dto.DsmShiftTotals = _aggregation.BuildDsmShiftTotals(summaryRows);

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
                DateTime repDate = classified.IsValid ? classified.BusinessDate : r.RepaymentDate.Date;
                if (repDate == date.Date)
                {
                    bool match = false;
                    if (!string.IsNullOrWhiteSpace(r.ShiftNumber) && string.Equals(r.ShiftNumber.Trim(), shiftType.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        match = true;
                    }
                    else if (shiftType == "B" && classified.SettlementWindow == "Day")
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
            if (!string.IsNullOrWhiteSpace(r.ReferenceDisplay) && r.ReferenceDisplay != "—")
            {
                refNo = r.ReferenceDisplay;
            }
            else
            {
                if (string.Equals(r.PaymentMode, "PhonePe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.PaymentMode, "Credit Card", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.PaymentMode, "PineLabs Card", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.PaymentMode, "PetroCard", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.PaymentMode, "Petro Card", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(r.CardTid)) parts.Add($"TID: {r.CardTid}");
                    if (!string.IsNullOrWhiteSpace(r.CardBatch)) parts.Add($"Batch: {r.CardBatch}");
                    refNo = parts.Count > 0 ? string.Join(", ", parts) : "";
                }
                else if (string.Equals(r.PaymentMode, "Bank Transfer", StringComparison.OrdinalIgnoreCase))
                {
                    refNo = !string.IsNullOrWhiteSpace(r.CardTid) ? $"Ref: {r.CardTid}" : "";
                }
                else if (string.Equals(r.PaymentMode, "Cheque", StringComparison.OrdinalIgnoreCase))
                {
                    refNo = !string.IsNullOrWhiteSpace(r.ChequeNo) ? $"Chq: {r.ChequeNo}" : (!string.IsNullOrWhiteSpace(r.CardTid) ? $"Chq: {r.CardTid}" : "");
                }
            }

            if (!r.CreditorName.Contains("DSM Loss", StringComparison.OrdinalIgnoreCase))
            {
                dto.DebtorRepayments.Add(new CreditorRepaymentPrintDto
                {
                    DebtorName = r.CreditorName,
                    CreditorName = r.CreditorName,
                    PaymentMode = r.PaymentMode,
                    RefNo = refNo,
                    ReferenceDisplay = refNo,
                    CardTid = r.CardTid ?? "",
                    CardBatch = r.CardBatch ?? "",
                    Tid = r.CardTid ?? "",
                    Batch = r.CardBatch ?? "",
                    ChequeNo = r.ChequeNo ?? "",
                    Amount = (decimal)r.Amount
                });
            }
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
        var oilDefResult = ExtractOilDefSales(entriesList, date, _serviceProvider);
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
            double qrSum = entry.QrPayments?.Sum(q => q.Amount) ?? 0;
            double qrM = entry.QrPayments?.Where(q => q.Slot == "Morning" || q.Slot == "Day" || string.IsNullOrEmpty(q.Slot)).Sum(q => q.Amount) ?? 0;
            double qrN = entry.QrPayments?.Where(q => q.Slot == "Night").Sum(q => q.Amount) ?? 0;

            if (shiftType == "B")
            {
                phonePeMorning += (pc?.PhonePeDay ?? 0) + qrSum;
                phonePeCardMorning += pc?.PhonePeCardDay ?? 0;
                creditCardMorning += pc?.CreditCardDay ?? 0;
                petroCardMorning += (pc?.PetroCardDay ?? 0) > 0 ? pc.PetroCardDay : (pc?.PetroCardMorning ?? 0);
                petroCard += (pc?.PetroCardDay ?? 0) > 0 ? pc.PetroCardDay : (pc?.PetroCardMorning ?? 0);
            }
            else
            {
                phonePeMorning += (pc?.PhonePeMorning ?? 0) + qrM;
                phonePeNight += (pc?.PhonePeNight ?? 0) + qrN;
                phonePeCardMorning += pc?.PhonePeCardMorning ?? 0;
                phonePeCardNight += pc?.PhonePeCardNight ?? 0;
                creditCardMorning += pc?.CreditCardMorning ?? 0;
                creditCardNight += pc?.CreditCardNight ?? 0;
                petroCardMorning += pc?.PetroCardMorning ?? 0;
                petroCardNight += pc?.PetroCardNight ?? 0;
                petroCard += (pc?.PetroCardMorning ?? 0) + (pc?.PetroCardNight ?? 0);
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

        // 9. Testing summary totals (Tank-Wise, excluding disabled testing / CNG)
        var testingByTank = new Dictionary<string, (double Amount, double Volume, double Rate, string FuelType)>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entriesList)
        {
            var distinctTesting = (entry.TestingEntries ?? new List<TestingEntry>())
                .GroupBy(t => !string.IsNullOrWhiteSpace(t.FuelType) && t.FuelType != "MS" && t.FuelType != "HSD" ? t.FuelType.Trim().ToUpperInvariant() : t.TestingId.ToString())
                .Select(g => g.Last())
                .ToList();

            foreach (var t in distinctTesting)
            {
                var tankName = PumpConfiguration.GetTestingTankCategory(t.FuelType, entry.PumpId, date.Date);
                if (!PumpConfiguration.IsTestingEnabledForTank(tankName))
                {
                    continue; // Skip tanks with HasTesting == false (e.g. CNG)
                }

                double tAmt = t.Amount > 0 ? (double)t.Amount : (double)(t.Litres * t.Rate);
                double tVol = (double)t.Litres;
                double tRate = t.Rate > 0 ? (double)t.Rate : (tVol > 0 ? tAmt / tVol : 0);

                if (testingByTank.TryGetValue(tankName, out var existing))
                {
                    testingByTank[tankName] = (existing.Amount + tAmt, existing.Volume + tVol, tRate > 0 ? tRate : existing.Rate, existing.FuelType);
                }
                else
                {
                    var fuelType = PumpConfiguration.GetFuelTypeDisplayName(entry.PumpId, int.TryParse(t.FuelType, out var n) ? n : 1, date.Date);
                    testingByTank[tankName] = (tAmt, tVol, tRate, fuelType);
                }
            }
        }

        var testingSummaryList = new List<TestingSummaryItem>();
        foreach (var kvp in testingByTank)
        {
            testingSummaryList.Add(new TestingSummaryItem
            {
                TankName = kvp.Key,
                FuelType = kvp.Value.FuelType,
                VolumeLitres = kvp.Value.Volume,
                Rate = kvp.Value.Rate,
                Amount = kvp.Value.Amount
            });
        }
        dto.TestingSummaryItems = testingSummaryList;
        double testingTotal = testingSummaryList.Sum(t => t.Amount);

        // DSR Presentation (Actual Customer Sales = Gross Meter Sales - Correctly Matched Testing)
        var (dsrSales, dsrLitres, dsrAmount) = BuildDsrFuelSales(dto.FuelSales, testingSummaryList);
        dto.DsrFuelSales = dsrSales;
        dto.DsrTotalFuelLitres = dsrLitres;
        dto.DsrTotalFuelAmount = dsrAmount;

        // 10. DSM Short calculation
        double totalDsmShort = CalculateDsmShort(entriesList);
        dto.TotalDsmShort = totalDsmShort;

        // 11. Standardized dynamic collection categories with audit breakdown (Oil & DEF sales excluded from Final Reconciliation)
        var colService = _serviceProvider?.GetService<ICollectionTypeService>();
        List<CollectionTypeMaster> allColTypes = new();
        if (colService != null)
        {
            try
            {
                var task = colService.GetAllCollectionTypesAsync();
                task.Wait();
                allColTypes = task.Result ?? new List<CollectionTypeMaster>();
            }
            catch { }
        }

        var phType = allColTypes.FirstOrDefault(c => string.Equals(c.Code?.Replace("_", "")?.Replace(" ", ""), "PHONEPE", StringComparison.OrdinalIgnoreCase));
        var ccType = allColTypes.FirstOrDefault(c => {
            string nc = c.Code?.ToUpper().Replace("_", "").Replace(" ", "") ?? "";
            return nc.Contains("PINELAB") || nc.Contains("CREDITCARD") || nc.Contains("CREDITDEBITCARD") || nc == "CARD";
        });
        var pcType = allColTypes.FirstOrDefault(c => {
            string nc = c.Code?.ToUpper().Replace("_", "").Replace(" ", "") ?? "";
            return nc.Contains("PETROCARD") || nc == "PETRO";
        });
        var cdType = allColTypes.FirstOrDefault(c => string.Equals(c.Code?.Replace("_", "")?.Replace(" ", ""), "CASHDEPOSIT", StringComparison.OrdinalIgnoreCase));

        bool phActive = phType?.IsActive ?? (allColTypes.Count == 0);
        bool ccActive = ccType?.IsActive ?? (allColTypes.Count == 0);
        bool pcActive = pcType?.IsActive ?? (allColTypes.Count == 0);
        bool cdActive = cdType?.IsActive ?? true;

        string phName = phType?.DisplayName ?? "PhonePe";
        string ccName = ccType?.DisplayName ?? "PineLab Card";
        string pcName = pcType?.DisplayName ?? "PetroCard";
        string cdName = cdType?.DisplayName ?? "Cash Deposit";

        var breakdownList = new List<CollectionCategoryDto>();

        if (cdActive)
        {
            breakdownList.Add(new CollectionCategoryDto
            {
                Category = cdType?.DisplayName ?? "Cash Deposit",
                Amount = finalCashDeposit,
                BaseAmount = dto.Cash1.GrandTotal,
                RecoveryAmount = dto.BankCashRepayments
            });
        }
        breakdownList.Add(new CollectionCategoryDto
        {
            Category = "Cash In Hand",
            Amount = finalCashInHand,
            BaseAmount = dto.Cash2.GrandTotal,
            RecoveryAmount = dto.CashRepayments
        });

        if (phActive)
        {
            breakdownList.Add(new CollectionCategoryDto
            {
                Category = phType?.DisplayName ?? "PhonePe Direct",
                Amount = finalPhonePeMorning + finalPhonePeNight,
                BaseAmount = phonePeMorning + phonePeNight,
                RecoveryAmount = dto.PhonePeRepayments
            });
        }

        if (ccActive)
        {
            breakdownList.Add(new CollectionCategoryDto
            {
                Category = ccType?.DisplayName ?? "PineLabs Card",
                Amount = finalCreditCardMorning + finalCreditCardNight + finalPhonePeCardMorning + finalPhonePeCardNight,
                BaseAmount = creditCardMorning + creditCardNight + phonePeCardMorning + phonePeCardNight,
                RecoveryAmount = dto.CreditCardRepayments
            });
        }

        if (pcActive)
        {
            breakdownList.Add(new CollectionCategoryDto
            {
                Category = pcType?.DisplayName ?? "PetroCard",
                Amount = finalPetroCard,
                BaseAmount = petroCard,
                RecoveryAmount = dto.PetroCardRepayments
            });
        }

        // All other enabled dynamic collection types from Dev side (always show with 0 if no entries)
        var builtInCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PHONEPE", "CREDIT_CARD", "PINELAB_CARD", "PETROCARD", "CASH_DEPOSIT", "OTHERS", "OTHER" };
        var dynamicActiveTypes = allColTypes
            .Where(t => t.IsActive && !builtInCodes.Contains(t.Code) && !string.Equals(t.Code?.Replace("_", ""), "OTHERS", StringComparison.OrdinalIgnoreCase) && !string.Equals(t.Code?.Replace("_", ""), "OTHER", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.DisplayOrder)
            .ToList();

        var entryDynamicItems = entriesList
            .SelectMany(e => e.PaymentCollection?.Items ?? Enumerable.Empty<PaymentCollectionItem>())
            .ToList();

        foreach (var t in dynamicActiveTypes)
        {
            var cleanTCode = t.Code?.Replace("_", "")?.Replace(" ", "") ?? "";
            double sum = entryDynamicItems
                .Where(i => string.Equals(i.CollectionTypeCode, t.Code, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(i.CollectionTypeCode?.Replace("_", "")?.Replace(" ", ""), cleanTCode, StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.Amount);

            breakdownList.Add(new CollectionCategoryDto
            {
                Category = t.DisplayName,
                Amount = sum,
                BaseAmount = sum,
                RecoveryAmount = 0
            });
        }

        // Include Others (for record purpose only)
        double totalOthers = entriesList.Sum(e => e.PaymentCollection?.Others ?? 0);
        if (totalOthers > 0 || allColTypes.Any(t => (string.Equals(t.Code?.Replace("_", ""), "OTHERS", StringComparison.OrdinalIgnoreCase) || string.Equals(t.Code?.Replace("_", ""), "OTHER", StringComparison.OrdinalIgnoreCase)) && t.IsActive))
        {
            var othType = allColTypes.FirstOrDefault(t => string.Equals(t.Code?.Replace("_", ""), "OTHERS", StringComparison.OrdinalIgnoreCase) || string.Equals(t.Code?.Replace("_", ""), "OTHER", StringComparison.OrdinalIgnoreCase));
            breakdownList.Add(new CollectionCategoryDto
            {
                Category = othType?.DisplayName ?? "Others",
                Amount = totalOthers,
                BaseAmount = totalOthers,
                RecoveryAmount = 0,
                IsInformational = true
            });
        }

        breakdownList.Add(new() { Category = "Debtors", Amount = dto.CreditorsTotal, BaseAmount = dto.CreditorsTotal, RecoveryAmount = 0 });
        double khandhareTotal = entriesList.SelectMany(e => e.KhandharePetroleumEntries ?? new List<KhandharePetroleumEntry>()).Sum(k => k.Amount);
        breakdownList.Add(new() { Category = "Expenses", Amount = dto.ExpensesTotal, BaseAmount = dto.ExpensesTotal, RecoveryAmount = 0 });
        if (khandhareTotal > 0)
        {
            breakdownList.Add(new() { Category = "Kandhare Petroleum", Amount = khandhareTotal, BaseAmount = khandhareTotal, RecoveryAmount = 0 });
        }

        foreach (var testItem in testingSummaryList.Where(t => t.Amount > 0 || t.VolumeLitres > 0))
        {
            breakdownList.Add(new CollectionCategoryDto
            {
                Category = $"{testItem.TankName} Testing",
                Amount = testItem.Amount,
                BaseAmount = testItem.Amount,
                RecoveryAmount = 0,
                Volume = testItem.VolumeLitres
            });
        }

        if (totalDsmShort != 0) breakdownList.Add(new() { Category = "DSM Short", Amount = totalDsmShort, BaseAmount = totalDsmShort, RecoveryAmount = 0 });

        dto.CollectionBreakdown = breakdownList;


        dto.ActualCollection = dto.CollectionBreakdown.Where(c => c.Category != "DSM Short" && !c.IsInformational && !string.Equals(c.Category, "Others", StringComparison.OrdinalIgnoreCase) && !string.Equals(c.Category, "Other", StringComparison.OrdinalIgnoreCase)).Sum(c => c.Amount);
        dto.ExpectedCollection = dto.TotalFuelAmount + reconcilableRecoveriesTotal;
        double rawDiff = dto.ActualCollection - dto.ExpectedCollection;
        dto.Difference = rawDiff < -0.01 ? -dto.TotalDsmShort : (rawDiff > 0.01 ? rawDiff : 0);
        dto.IsBalanced = Math.Abs(dto.Difference) < 0.01;
        dto.BalancedStatus = dto.IsBalanced ? "Balanced" : (dto.Difference < 0 ? "Short" : "Excess");

        var personalDebtorsList = new List<DsmPersonalDebtorPrintDto>();
        foreach (var e in entriesList)
        {
            if (e.PersonalDebtors != null && e.PersonalDebtors.Count > 0)
            {
                personalDebtorsList.AddRange(e.PersonalDebtors.Select(pd => new DsmPersonalDebtorPrintDto
                {
                    DsmName = pd.DsmName,
                    FuelProduct = pd.FuelProduct ?? string.Empty,
                    Remarks = pd.Remarks ?? string.Empty,
                    Amount = pd.Amount
                }));
            }
            else
            {
                var (_, _, mismatch) = CalculateEntryTotals(e);
                if (mismatch < -0.01 && !e.ReconciledToPumpId.HasValue)
                {
                    personalDebtorsList.Add(new DsmPersonalDebtorPrintDto
                    {
                        DsmName = e.DsmName,
                        FuelProduct = "Fuel",
                        Remarks = $"Shift Shortage (Pump {e.PumpId})",
                        Amount = Math.Abs(mismatch)
                    });
                }
            }
        }
        dto.PersonalDebtors = personalDebtorsList;

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

        dto.QrPayments = entriesList
            .SelectMany(e => e.QrPayments ?? new List<DsmQrPaymentEntry>())
            .Select(q => new DsmQrPaymentPrintDto
            {
                DsmName = q.DsmName,
                TargetDsmName = q.TargetDsmName,
                Amount = q.Amount,
                Tid = q.Tid ?? string.Empty,
                Batch = q.Batch ?? string.Empty,
                Slot = q.Slot ?? string.Empty
            })
            .ToList();

        dto.PersonalDebtorRepayments = repaymentsList
            .Where(r => r.CreditorName.Contains("DSM Loss", StringComparison.OrdinalIgnoreCase))
            .Select(r =>
            {
                string dsmName = (r.CreditorName ?? "")
                    .Replace("(DSM Loss)", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("DSM Loss", "", StringComparison.OrdinalIgnoreCase)
                    .Trim(' ', '(', ')');
                return new DsmPersonalDebtorRepaymentPrintDto
                {
                    DsmName = string.IsNullOrEmpty(dsmName) ? "DSM" : dsmName,
                    PaymentMethod = r.PaymentMode ?? "Cash",
                    RefNo = r.ReferenceDisplay ?? "",
                    Amount = r.Amount
                };
            })
            .ToList();

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

        // 1. Fuel Sales (Table E / DSR)
        var entriesList = MergeConnectedPumpEntries(entries ?? new List<DsmEntry>());
        var todayEntries = entriesList.Where(e => {
            var d = e.Shift != null ? e.Shift.ShiftDate.Date : (e.CreatedAt != default ? e.CreatedAt.Date : startDate.Date);
            return d >= startDate.Date && d <= endDate.Date;
        }).ToList();

        dto.FuelSales = BuildDynamicFuelSales(todayEntries, hsdRate, msIRate, msIIRate, cngRate, startDate);
        dto.TotalFuelLitres = dto.FuelSales.Sum(f => f.Litres);
        dto.TotalFuelAmount = dto.FuelSales.Sum(f => f.Amount);
        dto.GrandTotalSaleAmount = dto.TotalFuelAmount;

        // 2. DSM Summary (Table A)
        var summaryRows = _aggregation.BuildDsmSummaryRows(todayEntries);
        dto.DsmSummaryRows = summaryRows;
        dto.DsmSummaryTotals = _aggregation.BuildDsmSummaryTotalRow(summaryRows);
        dto.DsmShiftTotals = _aggregation.BuildDsmShiftTotals(summaryRows);

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
                DateTime repDate = classified.IsValid ? classified.BusinessDate : r.RepaymentDate.Date;
                if (repDate >= startDate.Date && repDate <= endDate.Date)
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
            if (!string.IsNullOrWhiteSpace(r.ReferenceDisplay) && r.ReferenceDisplay != "—")
            {
                refNo = r.ReferenceDisplay;
            }
            else
            {
                if (string.Equals(r.PaymentMode, "PhonePe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.PaymentMode, "Credit Card", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.PaymentMode, "PineLabs Card", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.PaymentMode, "PetroCard", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.PaymentMode, "Petro Card", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(r.CardTid)) parts.Add($"TID: {r.CardTid}");
                    if (!string.IsNullOrWhiteSpace(r.CardBatch)) parts.Add($"Batch: {r.CardBatch}");
                    refNo = parts.Count > 0 ? string.Join(", ", parts) : "";
                }
                else if (string.Equals(r.PaymentMode, "Bank Transfer", StringComparison.OrdinalIgnoreCase))
                {
                    refNo = !string.IsNullOrWhiteSpace(r.CardTid) ? $"Ref: {r.CardTid}" : "";
                }
                else if (string.Equals(r.PaymentMode, "Cheque", StringComparison.OrdinalIgnoreCase))
                {
                    refNo = !string.IsNullOrWhiteSpace(r.ChequeNo) ? $"Chq: {r.ChequeNo}" : (!string.IsNullOrWhiteSpace(r.CardTid) ? $"Chq: {r.CardTid}" : "");
                }
            }

            if (!r.CreditorName.Contains("DSM Loss", StringComparison.OrdinalIgnoreCase))
            {
                dto.DebtorRepayments.Add(new CreditorRepaymentPrintDto
                {
                    DebtorName = r.CreditorName,
                    CreditorName = r.CreditorName,
                    PaymentMode = r.PaymentMode,
                    RefNo = refNo,
                    ReferenceDisplay = refNo,
                    CardTid = r.CardTid ?? "",
                    CardBatch = r.CardBatch ?? "",
                    Tid = r.CardTid ?? "",
                    Batch = r.CardBatch ?? "",
                    ChequeNo = r.ChequeNo ?? "",
                    Amount = (decimal)r.Amount
                });
            }
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
            double qrSum = entry.QrPayments?.Sum(q => q.Amount) ?? 0;
            double qrM = entry.QrPayments?.Where(q => q.Slot == "Morning" || q.Slot == "Day" || string.IsNullOrEmpty(q.Slot)).Sum(q => q.Amount) ?? 0;
            double qrN = entry.QrPayments?.Where(q => q.Slot == "Night").Sum(q => q.Amount) ?? 0;

            var rawType = entry.Shift?.ShiftType ?? "";
            var sType = (rawType == "I" || rawType == "Shift I") ? "A" : (rawType == "II" || rawType == "Shift II") ? "B" : (rawType == "III" || rawType == "Shift III") ? "C" : rawType;

            if (sType == "B")
            {
                phonePeDirectDay += (pc?.PhonePeDay ?? 0) + qrSum;
            }
            else
            {
                phonePeDirectMorning += (pc?.PhonePeMorning ?? 0) + qrM;
                phonePeDirectNight += (pc?.PhonePeNight ?? 0) + qrN;
            }

            phonePeCardMorning += pc?.PhonePeCardMorning ?? 0;
            pineLabsCardMorning += pc?.CreditCardMorning ?? 0;
            petroCardMorning += pc?.PetroCardMorning ?? 0;
            phonePeCardNight += pc?.PhonePeCardNight ?? 0;
            pineLabsCardNight += pc?.CreditCardNight ?? 0;
            petroCardNight += pc?.PetroCardNight ?? 0;
            phonePeCardDay += pc?.PhonePeCardDay ?? 0;
            pineLabsCardDay += pc?.CreditCardDay ?? 0;
            petroCardDay += pc?.PetroCardDay ?? 0;
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

        // 9. Testing summary totals (Tank-Wise, excluding disabled testing / CNG)
        var dayTestingByTank = new Dictionary<string, (double Amount, double Volume, double Rate, string FuelType)>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in todayEntries)
        {
            var distinctTesting = (entry.TestingEntries ?? new List<TestingEntry>())
                .GroupBy(t => !string.IsNullOrWhiteSpace(t.FuelType) && t.FuelType != "MS" && t.FuelType != "HSD" ? t.FuelType.Trim().ToUpperInvariant() : t.TestingId.ToString())
                .Select(g => g.Last())
                .ToList();

            foreach (var t in distinctTesting)
            {
                var tankName = PumpConfiguration.GetTestingTankCategory(t.FuelType, entry.PumpId, startDate.Date);
                if (!PumpConfiguration.IsTestingEnabledForTank(tankName))
                {
                    continue; // Skip tanks with HasTesting == false (e.g. CNG)
                }

                double tAmt = t.Amount > 0 ? (double)t.Amount : (double)(t.Litres * t.Rate);
                double tVol = (double)t.Litres;
                double tRate = t.Rate > 0 ? (double)t.Rate : (tVol > 0 ? tAmt / tVol : 0);

                if (dayTestingByTank.TryGetValue(tankName, out var existing))
                {
                    dayTestingByTank[tankName] = (existing.Amount + tAmt, existing.Volume + tVol, tRate > 0 ? tRate : existing.Rate, existing.FuelType);
                }
                else
                {
                    var fuelType = PumpConfiguration.GetFuelTypeDisplayName(entry.PumpId, int.TryParse(t.FuelType, out var n) ? n : 1, startDate.Date);
                    dayTestingByTank[tankName] = (tAmt, tVol, tRate, fuelType);
                }
            }
        }

        var dayTestingSummaryList = new List<TestingSummaryItem>();
        foreach (var kvp in dayTestingByTank)
        {
            dayTestingSummaryList.Add(new TestingSummaryItem
            {
                TankName = kvp.Key,
                FuelType = kvp.Value.FuelType,
                VolumeLitres = kvp.Value.Volume,
                Rate = kvp.Value.Rate,
                Amount = kvp.Value.Amount
            });
        }
        dto.TestingSummaryItems = dayTestingSummaryList;
        double testingTotal = dayTestingSummaryList.Sum(t => t.Amount);

        // Day DSR Presentation (Actual Customer Sales = Gross Meter Sales - Correctly Matched Testing)
        var (dayDsrSales, dayDsrLitres, dayDsrAmount) = BuildDsrFuelSales(dto.FuelSales, dayTestingSummaryList);
        dto.DsrFuelSales = dayDsrSales;
        dto.DsrTotalFuelLitres = dayDsrLitres;
        dto.DsrTotalFuelAmount = dayDsrAmount;

        // 10. DSM Short calculation
        double totalDsmShort = CalculateDsmShort(todayEntries);
        dto.TotalDsmShort = totalDsmShort;

        // 11. Oil & DEF Sales
        var oilDefResult = ExtractOilDefSales(todayEntries, startDate, _serviceProvider);
        dto.OilDefSales = oilDefResult.Rows;
        dto.OilDefSalesTotal = oilDefResult.Total;

        // 12. Build standardized dynamic collection categories with audit breakdown (Oil & DEF sales excluded from Final Reconciliation)
        var colService = _serviceProvider?.GetService<ICollectionTypeService>();
        List<CollectionTypeMaster> allColTypes = new();
        if (colService != null)
        {
            try
            {
                var task = colService.GetAllCollectionTypesAsync();
                task.Wait();
                allColTypes = task.Result;
            }
            catch { }
        }

        var phType = allColTypes.FirstOrDefault(c => string.Equals(c.Code?.Replace("_", "")?.Replace(" ", ""), "PHONEPE", StringComparison.OrdinalIgnoreCase));
        var ccType = allColTypes.FirstOrDefault(c => {
            string nc = c.Code?.ToUpper().Replace("_", "").Replace(" ", "") ?? "";
            return nc.Contains("PINELAB") || nc.Contains("CREDITCARD") || nc.Contains("CREDITDEBITCARD") || nc == "CARD";
        });
        var pcType = allColTypes.FirstOrDefault(c => {
            string nc = c.Code?.ToUpper().Replace("_", "").Replace(" ", "") ?? "";
            return nc.Contains("PETROCARD") || nc == "PETRO";
        });
        var cdType = allColTypes.FirstOrDefault(c => string.Equals(c.Code?.Replace("_", "")?.Replace(" ", ""), "CASHDEPOSIT", StringComparison.OrdinalIgnoreCase));

        bool phActive = phType?.IsActive ?? (allColTypes.Count == 0);
        bool ccActive = ccType?.IsActive ?? (allColTypes.Count == 0);
        bool pcActive = pcType?.IsActive ?? (allColTypes.Count == 0);
        bool cdActive = cdType?.IsActive ?? true;

        string phName = phType?.DisplayName ?? "PhonePe Direct";
        string ccName = ccType?.DisplayName ?? "PineLabs Card";
        string pcName = pcType?.DisplayName ?? "PetroCard";
        string cdName = cdType?.DisplayName ?? "Cash Deposit";

        var dayBreakdown = new List<CollectionCategoryDto>();
        if (cdActive)
        {
            dayBreakdown.Add(new() { Category = cdName, Amount = dto.Cash1.GrandTotal, BaseAmount = dto.Cash1.GrandTotal, RecoveryAmount = 0 });
        }
        dayBreakdown.Add(new() { Category = "Cash In Hand", Amount = dto.Cash2.GrandTotal + dto.CashRepayments, BaseAmount = dto.Cash2.GrandTotal, RecoveryAmount = dto.CashRepayments });

        if (phActive)
        {
            double phTotal = finalPhonePeMorning + finalPhonePeNight;
            dayBreakdown.Add(new() { Category = phName, Amount = phTotal, BaseAmount = phonePeDirectMorning + phonePeDirectDay + phonePeDirectNight, RecoveryAmount = dto.PhonePeRepayments });
        }

        if (ccActive)
        {
            double ccTotal = finalCreditCardMorning + finalCreditCardNight;
            dayBreakdown.Add(new() { Category = ccName, Amount = ccTotal, BaseAmount = pineLabsCardMorning + pineLabsCardDay + pineLabsCardNight, RecoveryAmount = dto.CreditCardRepayments });
        }

        if (pcActive)
        {
            double pcTotal = dto.PetroCardMorning + dto.PetroCardNight;
            dayBreakdown.Add(new() { Category = pcName, Amount = pcTotal, BaseAmount = petroCardMorning + petroCardDay + petroCardNight, RecoveryAmount = dto.PetroCardRepayments });
        }

        // All other enabled dynamic collection types from Dev side (always show with 0 if no entries)
        var builtInCodesDay = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PHONEPE", "CREDIT_CARD", "PINELAB_CARD", "PETROCARD", "CASH_DEPOSIT", "OTHERS", "OTHER" };

        var dynamicActiveTypesDay = allColTypes
            .Where(t => t.IsActive && !builtInCodesDay.Contains(t.Code) && !string.Equals(t.Code?.Replace("_", ""), "OTHERS", StringComparison.OrdinalIgnoreCase) && !string.Equals(t.Code?.Replace("_", ""), "OTHER", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.DisplayOrder)
            .ToList();

        var todayDynamicItems = todayEntries
            .SelectMany(e => e.PaymentCollection?.Items ?? Enumerable.Empty<PaymentCollectionItem>())
            .ToList();

        foreach (var t in dynamicActiveTypesDay)
        {
            var cleanTCode = t.Code?.Replace("_", "")?.Replace(" ", "") ?? "";
            double sum = todayDynamicItems
                .Where(i => string.Equals(i.CollectionTypeCode, t.Code, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(i.CollectionTypeCode?.Replace("_", "")?.Replace(" ", ""), cleanTCode, StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.Amount);

            dayBreakdown.Add(new CollectionCategoryDto
            {
                Category = t.DisplayName,
                Amount = sum,
                BaseAmount = sum,
                RecoveryAmount = 0
            });
        }

        // Include Others (for record purpose only)
        double totalDayOthers = todayEntries.Sum(e => e.PaymentCollection?.Others ?? 0);
        if (totalDayOthers > 0 || allColTypes.Any(t => (string.Equals(t.Code?.Replace("_", ""), "OTHERS", StringComparison.OrdinalIgnoreCase) || string.Equals(t.Code?.Replace("_", ""), "OTHER", StringComparison.OrdinalIgnoreCase)) && t.IsActive))
        {
            var othType = allColTypes.FirstOrDefault(t => string.Equals(t.Code?.Replace("_", ""), "OTHERS", StringComparison.OrdinalIgnoreCase) || string.Equals(t.Code?.Replace("_", ""), "OTHER", StringComparison.OrdinalIgnoreCase));
            dayBreakdown.Add(new CollectionCategoryDto
            {
                Category = othType?.DisplayName ?? "Others",
                Amount = totalDayOthers,
                BaseAmount = totalDayOthers,
                RecoveryAmount = 0,
                IsInformational = true
            });
        }

        dayBreakdown.Add(new() { Category = "Debtors", Amount = dto.CreditorsTotal, BaseAmount = dto.CreditorsTotal, RecoveryAmount = 0 });
        dayBreakdown.Add(new() { Category = "Expenses", Amount = dto.ExpensesTotal, BaseAmount = dto.ExpensesTotal, RecoveryAmount = 0 });

        foreach (var testItem in dayTestingSummaryList.Where(t => t.Amount > 0 || t.VolumeLitres > 0))
        {
            dayBreakdown.Add(new CollectionCategoryDto
            {
                Category = $"{testItem.TankName} Testing",
                Amount = testItem.Amount,
                BaseAmount = testItem.Amount,
                RecoveryAmount = 0,
                Volume = testItem.VolumeLitres
            });
        }

        if (totalDsmShort != 0) dayBreakdown.Add(new() { Category = "DSM Short", Amount = totalDsmShort, BaseAmount = totalDsmShort, RecoveryAmount = 0 });

        double khandhareTotal = todayEntries.SelectMany(e => e.KhandharePetroleumEntries ?? new List<KhandharePetroleumEntry>()).Sum(k => k.Amount);
        if (khandhareTotal > 0)
        {
            dayBreakdown.Add(new() { Category = "Kandhare Petroleum", Amount = khandhareTotal, BaseAmount = khandhareTotal, RecoveryAmount = 0 });
        }

        dto.CollectionBreakdown = dayBreakdown;

        // 12. Final Reconciliation
        dto.ActualCollection = dto.CollectionBreakdown.Where(c => c.Category != "DSM Short" && !c.IsInformational && !string.Equals(c.Category, "Others", StringComparison.OrdinalIgnoreCase) && !string.Equals(c.Category, "Other", StringComparison.OrdinalIgnoreCase)).Sum(c => c.Amount);
        dto.ExpectedCollection = dto.TotalFuelAmount + reconcilableRecoveriesTotal;
        double rawDiff = dto.ActualCollection - dto.ExpectedCollection;
        dto.Difference = rawDiff < -0.01 ? -dto.TotalDsmShort : (rawDiff > 0.01 ? rawDiff : 0);
        dto.IsBalanced = Math.Abs(dto.Difference) < 0.01;
        dto.BalancedStatus = dto.IsBalanced ? "Balanced" : (dto.Difference < 0 ? "Short" : "Excess");

        var dayPersonalDebtorsList = new List<DsmPersonalDebtorPrintDto>();
        foreach (var e in todayEntries)
        {
            if (e.PersonalDebtors != null && e.PersonalDebtors.Count > 0)
            {
                dayPersonalDebtorsList.AddRange(e.PersonalDebtors.Select(pd => new DsmPersonalDebtorPrintDto
                {
                    DsmName = pd.DsmName,
                    FuelProduct = pd.FuelProduct ?? string.Empty,
                    Remarks = pd.Remarks ?? string.Empty,
                    Amount = pd.Amount
                }));
            }
            else
            {
                var (_, _, mismatch) = CalculateEntryTotals(e);
                if (mismatch < -0.01 && !e.ReconciledToPumpId.HasValue)
                {
                    dayPersonalDebtorsList.Add(new DsmPersonalDebtorPrintDto
                    {
                        DsmName = e.DsmName,
                        FuelProduct = "Fuel",
                        Remarks = $"Shift Shortage (Pump {e.PumpId})",
                        Amount = Math.Abs(mismatch)
                    });
                }
            }
        }
        dto.PersonalDebtors = dayPersonalDebtorsList;

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

        dto.QrPayments = todayEntries
            .SelectMany(e => e.QrPayments ?? new List<DsmQrPaymentEntry>())
            .Select(q => new DsmQrPaymentPrintDto
            {
                DsmName = q.DsmName,
                TargetDsmName = q.TargetDsmName,
                Amount = q.Amount,
                Tid = q.Tid ?? string.Empty,
                Batch = q.Batch ?? string.Empty,
                Slot = q.Slot ?? string.Empty
            })
            .ToList();

        dto.PersonalDebtorRepayments = repaymentsList
            .Where(r => r.CreditorName.Contains("DSM Loss", StringComparison.OrdinalIgnoreCase))
            .Select(r =>
            {
                string dsmName = (r.CreditorName ?? "")
                    .Replace("(DSM Loss)", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("DSM Loss", "", StringComparison.OrdinalIgnoreCase)
                    .Trim(' ', '(', ')');
                return new DsmPersonalDebtorRepaymentPrintDto
                {
                    DsmName = string.IsNullOrEmpty(dsmName) ? "DSM" : dsmName,
                    PaymentMethod = r.PaymentMode ?? "Cash",
                    RefNo = r.ReferenceDisplay ?? "",
                    Amount = r.Amount
                };
            })
            .ToList();

        return dto;
    }

    public static (double GrossSales, double TotalCollection, double Mismatch) CalculateEntryTotals(DsmEntry e)
    {
        double gs = (double)e.GrossSales > 0 
            ? (double)e.GrossSales 
            : (e.NozzleReadings != null && e.NozzleReadings.Count > 0 ? e.NozzleReadings.Sum(n => n.Amount) : 0);

        double cash1 = e.CashDenominations != null && e.CashDenominations.Any(c => c.CashType == "Cash1")
            ? e.CashDenominations.Where(c => c.CashType == "Cash1").Sum(c => c.TotalAmount)
            : (e.PaymentCollection?.CashDeposit ?? 0);

        double cash2 = e.CashDenominations != null && e.CashDenominations.Any(c => c.CashType == "Cash2")
            ? e.CashDenominations.Where(c => c.CashType == "Cash2").Sum(c => c.TotalAmount)
            : 0;

        double phM = e.PaymentCollection?.PhonePeMorning ?? 0;
        double phD = e.PaymentCollection?.PhonePeDay ?? 0;
        double phN = e.PaymentCollection?.PhonePeNight ?? 0;
        double ph = e.PaymentCollection?.PhonePe ?? 0;
        double phTotal = (phM + phD + phN) > 0 ? (phM + phD + phN) : ph;

        double ppcM = e.PaymentCollection?.PhonePeCardMorning ?? 0;
        double ppcD = e.PaymentCollection?.PhonePeCardDay ?? 0;
        double ppcN = e.PaymentCollection?.PhonePeCardNight ?? 0;
        double ppc = e.PaymentCollection?.PhonePeCard ?? 0;
        double ppcTotal = (ppcM + ppcD + ppcN) > 0 ? (ppcM + ppcD + ppcN) : ppc;

        double ccM = e.PaymentCollection?.CreditCardMorning ?? 0;
        double ccD = e.PaymentCollection?.CreditCardDay ?? 0;
        double ccN = e.PaymentCollection?.CreditCardNight ?? 0;
        double cc = e.PaymentCollection?.CreditCard ?? 0;
        double ccTotal = (ccM + ccD + ccN) > 0 ? (ccM + ccD + ccN) : cc;

        double petroM = e.PaymentCollection?.PetroCardMorning ?? 0;
        double petroD = e.PaymentCollection?.PetroCardDay ?? 0;
        double petroN = e.PaymentCollection?.PetroCardNight ?? 0;
        double petro = e.PaymentCollection?.PetroCard ?? 0;
        double petroTotal = (petroM + petroD + petroN) > 0 ? (petroM + petroD + petroN) : petro;

        double dynamicColl = e.PaymentCollection?.Items?.Sum(i => i.Amount) ?? 0;
        double debits = e.DebitEntries?.Sum(d => d.Amount) ?? 0;
        double expenses = (e.Expenses?.Sum(x => x.Amount) ?? 0) + (e.KhandharePetroleumEntries?.Sum(kp => kp.Amount) ?? 0);
        double testing = e.TestingEntries?.Sum(t => t.Amount) ?? 0;

        double totalCollection = cash1 + cash2 + phTotal + ppcTotal + ccTotal + petroTotal + dynamicColl + debits + expenses;
        double netGrossSales = gs - testing;
        double mismatch = totalCollection - netGrossSales;

        return (gs, totalCollection, mismatch);
    }

    private double CalculateDsmShort(List<DsmEntry> entries)
    {
        double totalDsmShort = 0;
        var merged = MergeConnectedPumpEntries(entries);
        foreach (var e in merged)
        {
            var (_, _, mismatch) = CalculateEntryTotals(e);

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
            var primaryConnectedPumps = primary.GetEffectiveConnectedPumpIds();
            var connectedSlaves = new List<DsmEntry>();

            foreach (var slavePumpId in primaryConnectedPumps)
            {
                var candidates = slaveEntries
                    .Where(s => !usedSlaveIds.Contains(s.DsmEntryId)
                        && s.PumpId == slavePumpId
                        && (s.ShiftId == primary.ShiftId || primary.ShiftId == 0 || s.ShiftId == 0))
                    .ToList();

                var bestSlave = candidates
                    .OrderBy(s => {
                        bool nameMatches = string.IsNullOrWhiteSpace(s.DsmName) || string.IsNullOrWhiteSpace(primary.DsmName)
                            || string.Equals((s.DsmName ?? "").Trim(), (primary.DsmName ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
                        bool explicitRecon = s.ReconciledToPumpId == primary.DsmEntryId;
                        bool notClaimed = !s.ReconciledToPumpId.HasValue || !primaryEntries.Any(p => p.DsmEntryId == s.ReconciledToPumpId.Value && p.DsmEntryId != primary.DsmEntryId);

                        if (nameMatches && explicitRecon) return 0;
                        if (nameMatches && notClaimed) return 1;
                        if (nameMatches) return 2;
                        if (explicitRecon) return 3;
                        if (!claimedByOther(s, primaryEntries, primary)) return 4;
                        return 5;

                        static bool claimedByOther(DsmEntry slave, List<DsmEntry> primaries, DsmEntry curPrimary) =>
                            slave.ReconciledToPumpId.HasValue && primaries.Any(p => p.DsmEntryId == slave.ReconciledToPumpId.Value && p.DsmEntryId != curPrimary.DsmEntryId);
                    })
                    .ThenBy(s => s.DsmEntryId >= primary.DsmEntryId ? (s.DsmEntryId - primary.DsmEntryId) : (100000 + Math.Abs(s.DsmEntryId - primary.DsmEntryId)))
                    .FirstOrDefault();

                if (bestSlave != null)
                {
                    usedSlaveIds.Add(bestSlave.DsmEntryId);
                    connectedSlaves.Add(bestSlave);
                }
            }

            var group = new List<DsmEntry> { primary };
            group.AddRange(connectedSlaves);

            var distinctNozzles = group
                .SelectMany(e => e.NozzleReadings ?? new List<NozzleReading>())
                .GroupBy(n => n.NozzleNumber)
                .Select(g => g.First())
                .ToList();

            decimal grossSales = distinctNozzles.Count > 0 
                ? (decimal)distinctNozzles.Sum(n => n.Amount)
                : (primary.GrossSales > 0 ? primary.GrossSales : (decimal)group.Sum(e => e.GrossSales));

            // Legacy first connected pump ID:
            int? firstSlavePumpId = primary.ConnectedPumpId ?? connectedSlaves.FirstOrDefault()?.PumpId;

            var merged = new DsmEntry
            {
                DsmEntryId = primary.DsmEntryId,
                ShiftId = primary.ShiftId,
                Shift = primary.Shift,
                DsmName = primary.DsmName,
                PumpId = primary.PumpId,
                ConnectedPumpId = firstSlavePumpId,
                ConnectedPumpIdsJson = primary.ConnectedPumpIdsJson ?? (connectedSlaves.Count > 0 ? JsonSerializer.Serialize(connectedSlaves.Select(s => s.PumpId).Distinct().ToList()) : null),
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

            // Child collections:
            // Nozzles: aggregated from primary + all slaves
            merged.NozzleReadings = distinctNozzles;

            // Financial collections: PRIMARY ONLY per Phase 3 rules, with graceful fallback to slave if primary has none
            merged.CashDenominations = (primary.CashDenominations != null && primary.CashDenominations.Count > 0)
                ? primary.CashDenominations
                : connectedSlaves.SelectMany(s => s.CashDenominations ?? new List<CashDenomination>()).ToList();
            merged.DebitEntries = (primary.DebitEntries != null && primary.DebitEntries.Count > 0)
                ? primary.DebitEntries
                : connectedSlaves.SelectMany(s => s.DebitEntries ?? new List<DebitEntry>()).ToList();
            merged.Expenses = (primary.Expenses != null && primary.Expenses.Count > 0)
                ? primary.Expenses
                : connectedSlaves.SelectMany(s => s.Expenses ?? new List<Expense>()).ToList();
            merged.TestingEntries = (primary.TestingEntries ?? new List<TestingEntry>())
                .Concat(connectedSlaves.SelectMany(s => s.TestingEntries ?? new List<TestingEntry>()))
                .ToList();
            merged.PersonalDebtors = (primary.PersonalDebtors != null && primary.PersonalDebtors.Count > 0)
                ? primary.PersonalDebtors
                : connectedSlaves.SelectMany(s => s.PersonalDebtors ?? new List<DsmPersonalDebtor>()).ToList();
            merged.KhandharePetroleumEntries = (primary.KhandharePetroleumEntries != null && primary.KhandharePetroleumEntries.Count > 0)
                ? primary.KhandharePetroleumEntries
                : connectedSlaves.SelectMany(s => s.KhandharePetroleumEntries ?? new List<KhandharePetroleumEntry>()).ToList();
            merged.QrPayments = (primary.QrPayments != null && primary.QrPayments.Count > 0)
                ? primary.QrPayments
                : connectedSlaves.SelectMany(s => s.QrPayments ?? new List<DsmQrPaymentEntry>()).ToList();
            merged.PaymentCollection = primary.PaymentCollection ?? connectedSlaves.FirstOrDefault(s => s.PaymentCollection != null)?.PaymentCollection;

            var (calcGs, calcTot, calcMis) = CalculateEntryTotals(merged);
            merged.GrossSales = (decimal)calcGs;
            merged.TotalCollection = (decimal)calcTot;
            merged.Mismatch = (decimal)calcMis;

            mergedEntries.Add(merged);
        }

        foreach (var slave in slaveEntries.Where(e => !usedSlaveIds.Contains(e.DsmEntryId)))
        {
            mergedEntries.Add(slave);
        }

        return mergedEntries;
    }

    public static (List<OilDefSaleDisplayRow> Rows, double Total) ExtractOilDefSales(IEnumerable<DsmEntry>? entries, DateTime? targetDate = null, IServiceProvider? sp = null)
    {
        var list = new List<OilDefSaleDisplayRow>();

        // 1. Try querying DbContext for OilDefDailyLogs
        try
        {
            IServiceProvider? provider = sp;
            if (provider == null)
            {
                var appType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "App");
                provider = appType?.GetProperty("Services")?.GetValue(null) as IServiceProvider;
            }

            if (provider != null)
            {
                using var scope = provider.CreateScope();
                var dbCtxType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "FuelProDbContext");

                var dbContext = (dbCtxType != null ? scope.ServiceProvider.GetService(dbCtxType) : null) as DbContext;
                if (dbContext != null)
                {
                    var dates = new List<DateTime>();
                    if (targetDate.HasValue) dates.Add(targetDate.Value.Date);
                    if (entries != null) dates.AddRange(entries.Select(e => e.Shift?.ShiftDate.Date ?? e.CreatedAt.Date).Distinct());
                    dates = dates.Distinct().ToList();

                    if (dates.Any())
                    {
                        var logs = dbContext.Set<OilDefDailyLog>()
                            .Include("Product")
                            .Where(l => dates.Contains(l.LogDate.Date) && l.SoldQuantity > 0)
                            .ToList();

                        if (logs.Any())
                        {
                            foreach (var log in logs)
                            {
                                double rate = log.OverrideSaleRate ?? log.Product?.DefaultSaleRate ?? 0;
                                double total = log.SoldQuantity * rate;
                                list.Add(new OilDefSaleDisplayRow
                                {
                                    ProductName = log.Product?.ProductName ?? log.ProductType ?? "Oil/DEF",
                                    Category = log.ProductType ?? "Oil",
                                    Quantity = log.SoldQuantity,
                                    Rate = rate,
                                    Total = total
                                });
                            }
                            return (list, list.Sum(x => x.Total));
                        }
                    }
                }
            }
        }
        catch { }

        // 2. Fallback to reflection on entry string properties
        if (entries != null)
        {
            foreach (var entry in entries)
            {
                var stringProps = entry.GetType().GetProperties()
                    .Where(p => p.PropertyType == typeof(string));

                foreach (var prop in stringProps)
                {
                    var val = prop.GetValue(entry) as string;
                    if (string.IsNullOrWhiteSpace(val) || !val.Contains("oilDefSales")) continue;

                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(val);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("oilDefSales", out var salesElement) && salesElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            ProcessSalesElement(salesElement, list);
                        }
                        else if (root.TryGetProperty("MetadataJson", out var metaElem))
                        {
                            if (metaElem.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                using var metaDoc = System.Text.Json.JsonDocument.Parse(metaElem.GetString()!);
                                if (metaDoc.RootElement.TryGetProperty("oilDefSales", out var sElem))
                                {
                                    ProcessSalesElement(sElem, list);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        return (list, list.Sum(x => x.Total));
    }

    private static void ProcessSalesElement(System.Text.Json.JsonElement salesElement, List<OilDefSaleDisplayRow> list)
    {
        if (salesElement.ValueKind != System.Text.Json.JsonValueKind.Array) return;
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


    private List<FuelSaleRowDto> BuildDynamicFuelSales(List<DsmEntry> entries, double hsdRate, double msIRate, double msIIRate, double cngRate, DateTime? reportDate = null)
    {
        var result = new List<FuelSaleRowDto>();
        var stationConfig = _serviceProvider?.GetService<IStationConfigurationService>();
        List<TankDefinition> dynamicTanks = new();
        List<PumpMapping> dynamicMappings = new();
        if (stationConfig != null)
        {
            try
            {
                var tanksResult = stationConfig.GetAllTanksAsync().GetAwaiter().GetResult();
                var mappingsResult = stationConfig.GetAllPumpMappingsAsync().GetAwaiter().GetResult();
                dynamicTanks = tanksResult.Where(t => t.IsActive).ToList();
                dynamicMappings = mappingsResult.Where(m => m.IsActive).ToList();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Non-fatal: Failed to load dynamic tanks/mappings from StationConfigurationService");
            }
        }

        if (dynamicTanks.Count == 0 && PumpConfiguration.NozzleTankMap.Count > 0)
        {
            var distinctTankNames = PumpConfiguration.NozzleTankMap.Values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct();
            foreach (var tName in distinctTankNames)
            {
                var fType = tName.Contains("MS", StringComparison.OrdinalIgnoreCase) ? "MS-I" :
                            tName.Contains("CNG", StringComparison.OrdinalIgnoreCase) ? "CNG" : "HSD";
                dynamicTanks.Add(new TankDefinition { TankName = tName, FuelType = fType, IsActive = true });
            }
        }

        var allReadings = entries
            .SelectMany(e => (e.NozzleReadings ?? Enumerable.Empty<NozzleReading>()).Select(r => new
            {
                PumpId = e.PumpId,
                Reading = r,
                TankName = (r.FuelType != null && r.FuelType.Contains("CNG", StringComparison.OrdinalIgnoreCase))
                    ? "CNG Line"
                    : PumpConfiguration.GetTankName(e.PumpId, r.NozzleNumber, e.Shift?.ShiftDate ?? reportDate),
                FuelTypeDisplayName = (r.FuelType != null && r.FuelType.Contains("CNG", StringComparison.OrdinalIgnoreCase))
                    ? "CNG"
                    : PumpConfiguration.GetFuelTypeDisplayName(e.PumpId, r.NozzleNumber, e.Shift?.ShiftDate ?? reportDate)
            }))
            .ToList();

        var allocatedReadings = new HashSet<object>();

        if (dynamicTanks.Count > 0)
        {
            foreach (var tank in dynamicTanks)
            {
                var isCng = string.Equals(tank.FuelType, "CNG", StringComparison.OrdinalIgnoreCase) || 
                            tank.TankName.Contains("CNG", StringComparison.OrdinalIgnoreCase);

                // Find all (PumpId, NozzleNumber) pairs and NozzleNumbers mapped to this tank
                var tankNozzlePairs = dynamicMappings
                    .Where(m => string.Equals(m.TankName, tank.TankName, StringComparison.OrdinalIgnoreCase))
                    .Select(m => (m.PumpId, m.NozzleNumber))
                    .ToHashSet();

                var tankNozzleNumbers = dynamicMappings
                    .Where(m => string.Equals(m.TankName, tank.TankName, StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.NozzleNumber)
                    .ToHashSet();

                var matchedReadings = allReadings
                    .Where(x => 
                        !allocatedReadings.Contains(x) &&
                        (
                            (tankNozzlePairs.Count > 0 && (tankNozzlePairs.Contains((x.PumpId, x.Reading.NozzleNumber)) || tankNozzleNumbers.Contains(x.Reading.NozzleNumber))) ||
                            string.Equals(x.TankName, tank.TankName, StringComparison.OrdinalIgnoreCase) ||
                            (!dynamicMappings.Any(m => m.NozzleNumber == x.Reading.NozzleNumber) &&
                                (
                                    (isCng && (string.Equals(x.Reading.FuelType, "CNG", StringComparison.OrdinalIgnoreCase) || (x.Reading.FuelType != null && x.Reading.FuelType.Contains("CNG", StringComparison.OrdinalIgnoreCase)))) ||
                                    (!isCng && string.Equals(x.Reading.FuelType, tank.FuelType, StringComparison.OrdinalIgnoreCase))
                                )
                            )
                        )
                        // Make sure we never assign CNG reading to an HSD/MS tank or vice versa
                        && (!isCng ? !(x.Reading.FuelType != null && x.Reading.FuelType.Contains("CNG", StringComparison.OrdinalIgnoreCase)) : true)
                    )
                    .ToList();

                foreach (var mr in matchedReadings)
                {
                    allocatedReadings.Add(mr);
                }

                var litres = matchedReadings.Sum(x => x.Reading.SaleLitres);
                var amount = matchedReadings.Sum(x => x.Reading.Amount);
                var settingsRepo = _serviceProvider?.GetService<ISettingsRepository>();
                var settings = settingsRepo != null ? settingsRepo.GetSettingsAsync().GetAwaiter().GetResult() : null;
                double defaultRate = 0;
                if (settings?.Success == true && settings.Data != null)
                {
                    defaultRate = settings.Data.GetRateFor(tank.TankName, 0);
                    if (defaultRate <= 0) defaultRate = settings.Data.GetRateFor(tank.FuelType, 0);
                }
                if (defaultRate <= 0)
                {
                    defaultRate = string.Equals(tank.FuelType, "MS-I", StringComparison.OrdinalIgnoreCase) ? msIRate :
                                  string.Equals(tank.FuelType, "MS-II", StringComparison.OrdinalIgnoreCase) ? msIIRate :
                                  isCng ? cngRate : hsdRate;
                }

                if (amount == 0 && litres > 0 && defaultRate > 0)
                {
                    amount = litres * defaultRate;
                }
                double rate = litres > 0 ? Math.Round(amount / litres, 2) : defaultRate;

                result.Add(new FuelSaleRowDto
                {
                    Description = tank.TankName,
                    FuelType = tank.FuelType,
                    Litres = litres,
                    Rate = rate,
                    Amount = amount
                });
            }

            // Also check for any unallocated readings (e.g. CNG readings if no CNG tank was added in tanks table)
            var unallocated = allReadings.Where(r => !allocatedReadings.Contains(r)).ToList();
            if (unallocated.Count > 0)
            {
                var groups = unallocated.GroupBy(r => {
                    bool isCng = r.Reading.FuelType != null && r.Reading.FuelType.Contains("CNG", StringComparison.OrdinalIgnoreCase);
                    if (isCng) return "CNG Line";
                    return !string.IsNullOrWhiteSpace(r.TankName) ? r.TankName : (!string.IsNullOrWhiteSpace(r.Reading.FuelType) ? r.Reading.FuelType : (r.FuelTypeDisplayName ?? "Other"));
                });
                foreach (var g in groups)
                {
                    var groupName = g.Key;
                    var litres = g.Sum(x => x.Reading.SaleLitres);
                    var amount = g.Sum(x => x.Reading.Amount);
                    bool isCngGroup = string.Equals(groupName, "CNG", StringComparison.OrdinalIgnoreCase) || groupName.Contains("CNG", StringComparison.OrdinalIgnoreCase);
                    double defaultRate = isCngGroup ? cngRate :
                                          string.Equals(groupName, "MS-I", StringComparison.OrdinalIgnoreCase) ? msIRate :
                                          string.Equals(groupName, "MS-II", StringComparison.OrdinalIgnoreCase) ? msIIRate : hsdRate;
                    if (amount == 0 && litres > 0 && defaultRate > 0) amount = litres * defaultRate;
                    double rate = litres > 0 ? Math.Round(amount / litres, 2) : defaultRate;

                    var displayFuelType = isCngGroup ? "CNG" : (g.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.FuelTypeDisplayName))?.FuelTypeDisplayName ?? (groupName.Contains("MS") ? "MS-I" : (groupName.Contains("SPEED") ? "SPEED" : (groupName.Contains("CNG") ? "CNG" : "HSD"))));

                    result.Add(new FuelSaleRowDto
                    {
                        Description = isCngGroup ? "CNG Line" : groupName,
                        FuelType = isCngGroup ? "CNG" : displayFuelType,
                        Litres = litres,
                        Rate = rate,
                        Amount = amount
                    });
                }
            }
        }
        else
        {
            // Standard fallback by fuel products
            (double hsdL, double hsdA) = _aggregation.GetFuelTotals(entries, "HSD", null);
            (double msIL, double msIA) = _aggregation.GetFuelTotals(entries, "MS-I", null);
            (double msIIL, double msIIA) = _aggregation.GetFuelTotals(entries, "MS-II", null);
            (double cngL, double cngA) = _aggregation.GetFuelTotals(entries, "CNG", null);

            if (hsdA == 0 && hsdL > 0) hsdA = hsdL * hsdRate;
            if (msIIA == 0 && msIIL > 0) msIIA = msIIL * msIIRate;
            if (msIA == 0 && msIL > 0) msIA = msIL * msIRate;
            if (cngA == 0 && cngL > 0) cngA = cngL * cngRate;

            if (hsdL > 0 || hsdA > 0)
                result.Add(new FuelSaleRowDto { Description = "HSD", FuelType = "HSD", Litres = hsdL, Rate = hsdL > 0 ? Math.Round(hsdA / hsdL, 2) : hsdRate, Amount = hsdA });
            if (msIL > 0 || msIA > 0)
                result.Add(new FuelSaleRowDto { Description = "MS", FuelType = "MS-I", Litres = msIL, Rate = msIL > 0 ? Math.Round(msIA / msIL, 2) : msIRate, Amount = msIA });
            if (msIIL > 0 || msIIA > 0)
                result.Add(new FuelSaleRowDto { Description = "MS-II", FuelType = "MS-II", Litres = msIIL, Rate = msIIL > 0 ? Math.Round(msIIA / msIIL, 2) : msIIRate, Amount = msIIA });
            if (cngL > 0 || cngA > 0)
                result.Add(new FuelSaleRowDto { Description = "CNG Line", FuelType = "CNG", Litres = cngL, Rate = cngL > 0 ? Math.Round(cngA / cngL, 2) : cngRate, Amount = cngA });
        }

        return result;
    }

    private static (List<FuelSaleRowDto> DsrSales, double TotalLitres, double TotalAmount) BuildDsrFuelSales(
        List<FuelSaleRowDto> meterFuelSales,
        List<TestingSummaryItem> testingItems)
    {
        var dsrList = new List<FuelSaleRowDto>();
        if (meterFuelSales == null || meterFuelSales.Count == 0)
        {
            return (dsrList, 0, 0);
        }

        var tests = testingItems != null ? new List<TestingSummaryItem>(testingItems) : new List<TestingSummaryItem>();
        var usedTests = new HashSet<TestingSummaryItem>();

        foreach (var fs in meterFuelSales)
        {
            // Match testing strictly to this tank using tank category mapping
            var matchedTests = tests.Where(t => !usedTests.Contains(t) &&
                IsTankMatch(t.TankName, fs.Description)
            ).ToList();

            foreach (var t in matchedTests)
            {
                usedTests.Add(t);
            }

            double testLitres = matchedTests.Sum(t => t.VolumeLitres);
            double testAmount = matchedTests.Sum(t => t.Amount);

            double dsrLitres = Math.Max(0, fs.Litres - testLitres);
            double dsrAmount = Math.Max(0, fs.Amount - testAmount);
            double dsrRate = dsrLitres > 0 ? Math.Round(dsrAmount / dsrLitres, 2) : fs.Rate;

            dsrList.Add(new FuelSaleRowDto
            {
                Description = fs.Description,
                FuelType = fs.FuelType,
                Litres = dsrLitres,
                Rate = dsrRate,
                Amount = dsrAmount
            });
        }

        double totalLitres = dsrList.Sum(f => f.Litres);
        double totalAmount = dsrList.Sum(f => f.Amount);
        return (dsrList, totalLitres, totalAmount);
    }

    private static bool IsTankMatch(string? testTankName, string? fuelDescription)
    {
        if (string.IsNullOrWhiteSpace(testTankName) || string.IsNullOrWhiteSpace(fuelDescription))
            return false;

        testTankName = testTankName.Trim();
        fuelDescription = fuelDescription.Trim();

        // 1. Exact match (e.g. "MS - 20KL" == "MS - 20KL", "HSD - 20KL" == "HSD - 20KL")
        if (string.Equals(testTankName, fuelDescription, StringComparison.OrdinalIgnoreCase))
            return true;

        // 2. Alphanumeric match (e.g. "MS - 20KL" vs "MS-20KL")
        if (NormalizeTankKey(testTankName) == NormalizeTankKey(fuelDescription))
            return true;

        // 3. Normalized fallback for short descriptions ("HSD", "MS", "MS-II")
        bool testHasII = testTankName.Contains("II", StringComparison.OrdinalIgnoreCase);
        bool fuelHasII = fuelDescription.Contains("II", StringComparison.OrdinalIgnoreCase);

        if (testHasII != fuelHasII)
            return false; // Prevent cross-tank matching between Tank I and Tank II

        if (testTankName.Contains("HSD", StringComparison.OrdinalIgnoreCase) && fuelDescription.Contains("HSD", StringComparison.OrdinalIgnoreCase))
            return true;

        if (testTankName.Contains("MS", StringComparison.OrdinalIgnoreCase) && fuelDescription.Contains("MS", StringComparison.OrdinalIgnoreCase))
            return true;

        if (testTankName.Contains("CNG", StringComparison.OrdinalIgnoreCase) && fuelDescription.Contains("CNG", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string NormalizeTankKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        return new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }
}
