using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Models;
using FuelPro.Core.Common;
using FuelPro.Core.Services;
using FuelPro.Data;

namespace FuelPro.Data.Services;

public class FinancialCalculationService : IFinancialCalculationService
{
    private readonly FuelProDbContext _dbContext;

    public FinancialCalculationService(FuelProDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<FinancialCalculationResult> CalculateFinancialsAsync(DateTime startDate, DateTime endDate)
    {
        var result = new FinancialCalculationResult
        {
            StartDate = startDate,
            EndDate = endDate
        };

        // 1. Fetch shifts within date range
        var shifts = await _dbContext.Shifts
            .Include(s => s.DsmEntries)
                .ThenInclude(d => d.NozzleReadings)
            .Include(s => s.Expenses)
            .Where(s => s.ShiftDate >= startDate.Date && s.ShiftDate <= endDate.Date)
            .ToListAsync();

        // 2. Fetch other cash adjustments
        var otherCash = await _dbContext.ShiftOtherCash
            .Where(o => o.ShiftDate >= startDate.Date && o.ShiftDate <= endDate.Date)
            .ToListAsync();

        // 3. Fetch margins sorted by effective date for historical lookup
        var margins = await _dbContext.FuelProfitMargins
            .OrderBy(m => m.EffectiveDate)
            .ToListAsync();

        // Helper to lookup historical margin
        double GetMargin(string fuelType, DateTime date)
        {
            var match = margins
                .Where(m => string.Equals(m.FuelType, fuelType, StringComparison.OrdinalIgnoreCase) && m.EffectiveDate <= date)
                .OrderByDescending(m => m.EffectiveDate)
                .FirstOrDefault();

            if (match != null) return match.MarginPerLitre;

            // Baseline fallbacks if no margin record was found before this date
            return fuelType.ToUpper() switch
            {
                "HSD" => 3.0,
                "MS-I" => 4.0,
                "MS-II" => 4.0,
                "CNG" => 2.5,
                _ => 0.0
            };
        }

        // Aggregate fuel litres and calculate profit
        foreach (var shift in shifts)
        {
            foreach (var entry in shift.DsmEntries)
            {
                foreach (var nr in entry.NozzleReadings)
                {
                    var ft = PumpConfiguration.GetFuelTypeDisplayName(entry.PumpId, nr.NozzleNumber, shift.ShiftDate);
                    var margin = GetMargin(ft, shift.ShiftDate);
                    var profit = nr.SaleLitres * margin;

                    if (ft == "HSD")
                    {
                        result.FuelProfit.HsdLitres += nr.SaleLitres;
                        result.FuelProfit.HsdProfit += profit;
                    }
                    else if (ft == "MS-I")
                    {
                        result.FuelProfit.MsILitres += nr.SaleLitres;
                        result.FuelProfit.MsIProfit += profit;
                    }
                    else if (ft == "MS-II")
                    {
                        result.FuelProfit.MsIILitres += nr.SaleLitres;
                        result.FuelProfit.MsIIProfit += profit;
                    }
                    else if (ft == "CNG")
                    {
                        result.FuelProfit.CngLitres += nr.SaleLitres;
                        result.FuelProfit.CngProfit += profit;
                    }
                }
            }
        }

        // Calculate average margin for display in the P&L details
        result.FuelProfit.HsdMargin = result.FuelProfit.HsdLitres > 0 
            ? result.FuelProfit.HsdProfit / result.FuelProfit.HsdLitres 
            : GetMargin("HSD", endDate);
        result.FuelProfit.MsIMargin = result.FuelProfit.MsILitres > 0 
            ? result.FuelProfit.MsIProfit / result.FuelProfit.MsILitres 
            : GetMargin("MS-I", endDate);
        result.FuelProfit.MsIIMargin = result.FuelProfit.MsIILitres > 0 
            ? result.FuelProfit.MsIIProfit / result.FuelProfit.MsIILitres 
            : GetMargin("MS-II", endDate);
        result.FuelProfit.CngMargin = result.FuelProfit.CngLitres > 0 
            ? result.FuelProfit.CngProfit / result.FuelProfit.CngLitres 
            : GetMargin("CNG", endDate);

        // 4. Calculate Oil & DEF Profit by aggregating daily logs and purchases directly (using ProductMaster)
        result.OilProfit = await CalculateProductProfitForCategoryAsync("Oil", startDate, endDate);
        result.DefProfit = await CalculateProductProfitForCategoryAsync("DEF", startDate, endDate);

        // 5. Calculate Total Operational Expenses (Pump Expenses) in range
        double entryExpenses = shifts.SelectMany(s => s.DsmEntries).SelectMany(e => e.Expenses).Sum(ex => ex.Amount);
        double shiftExpenses = shifts.SelectMany(s => s.Expenses).Sum(ex => ex.Amount);
        result.TotalExpenses = entryExpenses + shiftExpenses;
        result.ManagerExpenses = entryExpenses + shiftExpenses;

        // 6. Calculate Total DSM Salary costs and components in range (prorated by month overlap)
        double totalSalaryCost = 0;
        double baseSalaries = 0;
        double adjustments = 0;
        double recoveries = 0;

        var startMonth = new DateTime(startDate.Year, startDate.Month, 1);
        var endMonth = new DateTime(endDate.Year, endDate.Month, 1);

        for (var current = startMonth; current <= endMonth; current = current.AddMonths(1))
        {
            var year = current.Year;
            var month = current.Month;

            var daysInMonth = DateTime.DaysInMonth(year, month);
            var monthStart = current;
            var monthEnd = new DateTime(year, month, daysInMonth);

            var overlapStart = startDate > monthStart ? startDate : monthStart;
            var overlapEnd = endDate < monthEnd ? endDate : monthEnd;

            var overlapDays = (overlapEnd.Date - overlapStart.Date).Days + 1;
            if (overlapDays <= 0) continue;

            var ratio = (double)overlapDays / daysInMonth;

            var salaries = await CalculateDsmSalariesAsync(year, month);
            totalSalaryCost += salaries.Sum(s => s.NetSalary) * ratio;
            baseSalaries += salaries.Sum(s => s.EarnedBase) * ratio;
            adjustments += salaries.Sum(s => s.OtherAdjustments) * ratio;
            recoveries += salaries.Sum(s => s.ShortRecovery) * ratio;
        }
        result.TotalDsmSalaries = Math.Round(totalSalaryCost, 2);
        result.DsmBaseSalaries = Math.Round(baseSalaries, 2);
        result.SalaryAdjustments = Math.Round(adjustments, 2);
        result.ShortRecoveries = Math.Round(recoveries, 2);

        // 7. Calculate Pump Expenses (Owner added) in range
        var pumpExps = await _dbContext.PumpExpenses
            .Where(e => e.ExpenseDate >= startDate.Date && e.ExpenseDate <= endDate.Date)
            .ToListAsync();

        result.PumpRent = Math.Round(pumpExps.Sum(e => e.Rent), 2);
        result.PumpSalary = Math.Round(pumpExps.Sum(e => e.Salary), 2);
        result.PumpTripSheetLoss = Math.Round(pumpExps.Sum(e => e.TripSheetLoss), 2);
        result.PumpDsmShort = Math.Round(pumpExps.Sum(e => e.DsmShort), 2);
        result.PumpBankingExpenses = Math.Round(pumpExps.Sum(e => e.BankingExpenses), 2);
        result.PumpBpclPortalExpenses = Math.Round(pumpExps.Sum(e => e.BpclPortalExpenses), 2);
        result.PumpFuelAndTravel = Math.Round(pumpExps.Sum(e => e.FuelAndTravel), 2);
        result.PumpOilPurchase = Math.Round(pumpExps.Sum(e => e.OilPurchase), 2);
        result.PumpRepairsAndMaintenance = Math.Round(pumpExps.Sum(e => e.RepairsAndMaintenance), 2);
        result.PumpElectricity = Math.Round(pumpExps.Sum(e => e.ElectricityExpenses), 2);
        result.PumpOfficeExpenses = Math.Round(pumpExps.Sum(e => e.OfficeExpenses), 2);
        result.PumpPrintingExpense = Math.Round(pumpExps.Sum(e => e.PrintingExpense), 2);
        result.PumpOtherAmount = Math.Round(pumpExps.Sum(e => e.OtherAmount), 2);

        // Fetch local-only Owner Outer Expenses in range
        var outerExpSum = await _dbContext.OuterExpenses
            .Where(e => e.ExpenseDate >= startDate.Date && e.ExpenseDate <= endDate.Date)
            .SumAsync(e => e.Amount);
        result.OwnerOuterExpenses = Math.Round(outerExpSum, 2);

        // Sum up mismatches from DsmEntries
        double totalMismatch = 0;
        foreach (var shift in shifts)
        {
            foreach (var entry in shift.DsmEntries)
            {
                totalMismatch += (double)entry.Mismatch;
            }
        }
        result.TotalMismatch = Math.Round(totalMismatch, 2);

        return result;
    }

    public async Task<List<DsmSalaryRowDto>> CalculateDsmSalariesAsync(int year, int month)
    {
        // Fetch all shifts in that month
        var shifts = await _dbContext.Shifts
            .Include(s => s.DsmEntries)
            .Where(s => s.ShiftDate.Year == year && s.ShiftDate.Month == month)
            .ToListAsync();

        // Get unique DSM names who worked
        var activeDsmNames = shifts
            .SelectMany(s => s.DsmEntries)
            .Select(e => e.DsmName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct()
            .ToList();

        // Get profiles
        var profiles = await _dbContext.DsmProfiles.ToListAsync();

        // Union to include all profile employees even if they did not work this month
        var allNames = activeDsmNames.Union(profiles.Select(p => p.DsmName)).Distinct().ToList();

        // Fetch salary adjustments
        var adjustments = await _dbContext.DsmSalaryAdjustments
            .Where(a => a.Year == year && a.Month == month)
            .ToListAsync();

        var allPersonalDebtors = await _dbContext.DsmPersonalDebtors
            .Where(d => d.Date.Year == year && d.Date.Month == month)
            .ToListAsync();

        var rows = new List<DsmSalaryRowDto>();

        foreach (var name in allNames)
        {
            var profile = profiles.FirstOrDefault(p => string.Equals(p.DsmName, name, StringComparison.OrdinalIgnoreCase));
            var dsmEntries = shifts
                .SelectMany(s => s.DsmEntries)
                .Where(e => string.Equals(e.DsmName, name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var salaryType = profile?.SalaryType ?? "FixedMonthly";
            var baseSalary = profile?.BaseSalary ?? 12000.0;
            var joiningDate = profile?.JoiningDate;

            var shiftsWorked = dsmEntries.Count;
            var uniqueDaysWorked = dsmEntries.Select(e => e.Shift != null ? e.Shift.ShiftDate.Date : e.CreatedAt.Date).Distinct().Count();

            // Short recovery is sum of absolute negative mismatches
            var shortRecovery = dsmEntries.Where(e => e.Mismatch < 0m).Sum(e => (double)Math.Abs(e.Mismatch));

            var adj = adjustments.FirstOrDefault(a => string.Equals(a.DsmName, name, StringComparison.OrdinalIgnoreCase));
            var advancePaid = adj?.AdvancePaid ?? 0.0;
            var otherAdjustments = adj?.OtherAdjustments ?? 0.0;
            var remarks = adj?.Remarks ?? string.Empty;

            double earnedBase = 0.0;
            if (string.Equals(salaryType, "FixedMonthly", StringComparison.OrdinalIgnoreCase))
            {
                if (joiningDate.HasValue)
                {
                    var join = joiningDate.Value;
                    if (join.Year == year && join.Month == month)
                    {
                        var daysInMonth = DateTime.DaysInMonth(year, month);
                        var daysWorked = daysInMonth - join.Day + 1;
                        if (daysWorked < 0) daysWorked = 0;
                        earnedBase = baseSalary * ((double)daysWorked / daysInMonth);
                    }
                    else if (join > new DateTime(year, month, DateTime.DaysInMonth(year, month)))
                    {
                        earnedBase = 0.0; // Hasn't joined yet in this month
                    }
                    else
                    {
                        earnedBase = baseSalary;
                    }
                }
                else
                {
                    earnedBase = baseSalary;
                }
            }
            else if (string.Equals(salaryType, "PerShift", StringComparison.OrdinalIgnoreCase))
            {
                earnedBase = baseSalary * shiftsWorked;
            }
            else if (string.Equals(salaryType, "PerDay", StringComparison.OrdinalIgnoreCase))
            {
                earnedBase = baseSalary * uniqueDaysWorked;
            }

            var personalDebtorDeduction = allPersonalDebtors
                .Where(d => string.Equals(d.DsmName, name, StringComparison.OrdinalIgnoreCase) && d.DeductFromSalary)
                .Sum(d => d.Amount - d.RepaidAmount);

            // Compute automatic Pending Advance deduction
            double pendingAdvanceDeduction = 0.0;
            double remainingPendingAdvance = 0.0;

            if (profile != null)
            {
                if (adj != null && adj.PendingAdvanceDeduction > 0)
                {
                    pendingAdvanceDeduction = adj.PendingAdvanceDeduction;
                    remainingPendingAdvance = profile.PendingAdvance;
                }
                else if (profile.PendingAdvance > 0)
                {
                    pendingAdvanceDeduction = Math.Min(profile.MonthlyAdvanceDeduction, profile.PendingAdvance);
                    remainingPendingAdvance = Math.Max(0.0, profile.PendingAdvance - pendingAdvanceDeduction);
                }
            }

            var netSalary = earnedBase - shortRecovery + otherAdjustments - advancePaid - personalDebtorDeduction - pendingAdvanceDeduction;

            rows.Add(new DsmSalaryRowDto
            {
                DsmName = name,
                SalaryType = salaryType,
                BaseSalary = baseSalary,
                JoiningDate = joiningDate,
                ShiftsWorked = shiftsWorked,
                UniqueDaysWorked = uniqueDaysWorked,
                EarnedBase = Math.Round(earnedBase, 2),
                ShortRecovery = Math.Round(shortRecovery, 2),
                AdvancePaid = Math.Round(advancePaid, 2),
                OtherAdjustments = Math.Round(otherAdjustments, 2),
                PersonalDebtorDeduction = Math.Round(personalDebtorDeduction, 2),
                PendingAdvanceDeduction = Math.Round(pendingAdvanceDeduction, 2),
                RemainingPendingAdvance = Math.Round(remainingPendingAdvance, 2),
                Remarks = remarks,
                NetSalary = Math.Round(netSalary, 2)
            });
        }

        return rows.OrderBy(r => r.DsmName).ToList();
    }

    public async Task<OilDefStockReportDto> GenerateStockReportAsync(int year, int month)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = new DateTime(year, month, DateTime.DaysInMonth(year, month));

        var oilDetail = await CalculateProductProfitForCategoryAsync("Oil", startDate, endDate);
        var defDetail = await CalculateProductProfitForCategoryAsync("DEF", startDate, endDate);

        return new OilDefStockReportDto
        {
            Year = year,
            Month = month,
            OilOpening = oilDetail.OpeningStock,
            OilClosing = oilDetail.ClosingStock,
            OilPurchasesQty = oilDetail.PurchasesQuantity,
            OilPurchasesCost = oilDetail.PurchasesCost,
            OilAvgPurchasePrice = oilDetail.AveragePurchasePrice,
            OilSalesQty = oilDetail.SalesQuantity,
            OilSalePrice = oilDetail.SalePrice,
            OilProfit = oilDetail.TotalProfit,

            DefOpening = defDetail.OpeningStock,
            DefClosing = defDetail.ClosingStock,
            DefPurchasesQty = defDetail.PurchasesQuantity,
            DefPurchasesCost = defDetail.PurchasesCost,
            DefAvgPurchasePrice = defDetail.AveragePurchasePrice,
            DefSalesQty = defDetail.SalesQuantity,
            DefSalePrice = defDetail.SalePrice,
            DefProfit = defDetail.TotalProfit
        };
    }

    private async Task<ProductProfitDetail> CalculateProductProfitForCategoryAsync(string category, DateTime startDate, DateTime endDate)
    {
        var categoryLower = category.ToLower();
        var products = await _dbContext.ProductMasters
            .Where(p => p.Category.ToLower() == categoryLower)
            .ToListAsync();

        var detail = new ProductProfitDetail
        {
            ProductType = category,
            OpeningStock = 0,
            ClosingStock = 0,
            PurchasesQuantity = 0,
            PurchasesCost = 0,
            SalesQuantity = 0,
            SalesRevenue = 0,
            CostOfGoodsSold = 0,
            TotalProfit = 0
        };

        foreach (var product in products)
        {
            // 1. Opening Stock: last daily log before startDate
            var lastLogBefore = await _dbContext.OilDefDailyLogs
                .Where(l => l.ProductId == product.Id && l.LogDate < startDate.Date)
                .OrderByDescending(l => l.LogDate)
                .FirstOrDefaultAsync();

            double opening = 0.0;
            if (lastLogBefore != null)
            {
                opening = lastLogBefore.RemainingStock;
            }
            else
            {
                // Fallback to monthly inventory table opening stock
                var monthInv = await _dbContext.OilDefInventories
                    .Where(i => i.ProductId == product.Id && i.Year == startDate.Year && i.Month == startDate.Month)
                    .FirstOrDefaultAsync();
                opening = monthInv?.OpeningStock ?? 0.0;
            }
            detail.OpeningStock += opening;

            // 2. Closing Stock: last daily log in range or before
            var lastLogInRange = await _dbContext.OilDefDailyLogs
                .Where(l => l.ProductId == product.Id && l.LogDate >= startDate.Date && l.LogDate <= endDate.Date)
                .OrderByDescending(l => l.LogDate)
                .FirstOrDefaultAsync();

            double closing = 0.0;
            if (lastLogInRange != null)
            {
                closing = lastLogInRange.RemainingStock;
            }
            else
            {
                closing = opening; // If no logs in range, closing is same as opening
            }
            detail.ClosingStock += closing;

            // 3. Purchases in range
            var purchases = await _dbContext.OilDefPurchases
                .Where(p => p.ProductId == product.Id && p.PurchaseDate >= startDate.Date && p.PurchaseDate <= endDate.Date)
                .ToListAsync();

            double purchasedQty = purchases.Sum(p => p.Quantity);
            double purchasedCost = purchases.Sum(p => p.TotalCost);
            detail.PurchasesQuantity += purchasedQty;
            detail.PurchasesCost += purchasedCost;

            // Average Purchase Price lookup
            double avgPurchasePrice = 0.0;
            if (purchasedQty > 0)
            {
                avgPurchasePrice = purchasedCost / purchasedQty;
            }
            else
            {
                var lastPurchase = await _dbContext.OilDefPurchases
                    .Where(p => p.ProductId == product.Id && p.PurchaseDate < startDate.Date)
                    .OrderByDescending(p => p.PurchaseDate)
                    .FirstOrDefaultAsync();
                avgPurchasePrice = lastPurchase?.UnitPrice ?? product.DefaultSaleRate * 0.8;
            }

            // 4. Sales in range
            var logs = await _dbContext.OilDefDailyLogs
                .Where(l => l.ProductId == product.Id && l.LogDate >= startDate.Date && l.LogDate <= endDate.Date)
                .ToListAsync();

            double salesQty = logs.Sum(l => l.SoldQuantity);
            double salesRevenue = logs.Sum(l => l.SoldQuantity * (l.OverrideSaleRate ?? product.DefaultSaleRate));

            detail.SalesQuantity += salesQty;
            detail.SalesRevenue += salesRevenue;

            double cogs = salesQty * avgPurchasePrice;
            detail.CostOfGoodsSold += cogs;

            double profit = salesRevenue - cogs;
            detail.TotalProfit += profit;
        }

        if (detail.PurchasesQuantity > 0)
        {
            detail.AveragePurchasePrice = detail.PurchasesCost / detail.PurchasesQuantity;
        }
        else
        {
            detail.AveragePurchasePrice = products.Any() ? products.Average(p => p.DefaultSaleRate * 0.8) : 0.0;
        }

        if (detail.SalesQuantity > 0)
        {
            detail.SalePrice = detail.SalesRevenue / detail.SalesQuantity;
        }
        else
        {
            detail.SalePrice = products.Any() ? products.Average(p => p.DefaultSaleRate) : 0.0;
        }

        detail.OpeningStock = Math.Round(detail.OpeningStock, 2);
        detail.ClosingStock = Math.Round(detail.ClosingStock, 2);
        detail.PurchasesQuantity = Math.Round(detail.PurchasesQuantity, 2);
        detail.PurchasesCost = Math.Round(detail.PurchasesCost, 2);
        detail.AveragePurchasePrice = Math.Round(detail.AveragePurchasePrice, 2);
        detail.SalesQuantity = Math.Round(detail.SalesQuantity, 2);
        detail.SalesRevenue = Math.Round(detail.SalesRevenue, 2);
        detail.CostOfGoodsSold = Math.Round(detail.CostOfGoodsSold, 2);
        detail.SalePrice = Math.Round(detail.SalePrice, 2);
        detail.TotalProfit = Math.Round(detail.TotalProfit, 2);

        return detail;
    }

    public async Task SaveDsmSalaryAdjustmentsAsync(int year, int month, List<DsmSalaryRowDto> rows)
    {
        var existingAdjustments = await _dbContext.DsmSalaryAdjustments
            .Where(a => a.Year == year && a.Month == month)
            .ToListAsync();

        foreach (var row in rows)
        {
            var adj = existingAdjustments.FirstOrDefault(a => string.Equals(a.DsmName, row.DsmName, StringComparison.OrdinalIgnoreCase));
            
            // Track pending advance deductions in DsmProfile
            var profile = await _dbContext.DsmProfiles.FirstOrDefaultAsync(p => string.Equals(p.DsmName, row.DsmName, StringComparison.OrdinalIgnoreCase));
            if (profile != null)
            {
                var previousDeduction = adj?.PendingAdvanceDeduction ?? 0.0;
                var netChange = row.PendingAdvanceDeduction - previousDeduction;
                if (netChange != 0)
                {
                    profile.PendingAdvance = Math.Max(0.0, profile.PendingAdvance - netChange);
                    _dbContext.Entry(profile).State = EntityState.Modified;
                }
            }

            if (adj != null)
            {
                adj.AdvancePaid = row.AdvancePaid;
                adj.OtherAdjustments = row.OtherAdjustments;
                adj.PendingAdvanceDeduction = row.PendingAdvanceDeduction;
                adj.Remarks = row.Remarks;
                _dbContext.Entry(adj).State = EntityState.Modified;
            }
            else
            {
                var newAdj = new DsmSalaryAdjustment
                {
                    DsmName = row.DsmName,
                    Year = year,
                    Month = month,
                    AdvancePaid = row.AdvancePaid,
                    OtherAdjustments = row.OtherAdjustments,
                    PendingAdvanceDeduction = row.PendingAdvanceDeduction,
                    Remarks = row.Remarks
                };
                _dbContext.DsmSalaryAdjustments.Add(newAdj);
            }

            // Repay personal debtors logged in the month through salary deduction
            if (row.PersonalDebtorDeduction > 0)
            {
                var outstandingDebtors = await _dbContext.DsmPersonalDebtors
                    .Where(d => d.Date.Year == year && d.Date.Month == month && string.Equals(d.DsmName, row.DsmName, StringComparison.OrdinalIgnoreCase) && d.RepaidAmount < d.Amount)
                    .ToListAsync();

                foreach (var debtor in outstandingDebtors)
                {
                    var unpaid = debtor.Amount - debtor.RepaidAmount;
                    if (unpaid <= 0) continue;

                    var repayment = new DsmPersonalDebtorRepayment
                    {
                        DsmPersonalDebtorId = debtor.Id,
                        Date = new DateTime(year, month, DateTime.DaysInMonth(year, month)),
                        Amount = unpaid,
                        PaymentMethod = "Payroll",
                        Source = "OwnerPayroll",
                        CreatedAt = DateTime.Now
                    };
                    _dbContext.DsmPersonalDebtorRepayments.Add(repayment);

                    debtor.RepaidAmount = debtor.Amount;
                    _dbContext.Entry(debtor).State = EntityState.Modified;
                }
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task DeleteDsmSalaryAdjustmentAsync(int year, int month, string dsmName)
    {
        var adj = await _dbContext.DsmSalaryAdjustments
            .FirstOrDefaultAsync(a => a.Year == year && a.Month == month && string.Equals(a.DsmName, dsmName, StringComparison.OrdinalIgnoreCase));
        if (adj != null)
        {
            // Refund the deducted pending advance back to profile
            var profile = await _dbContext.DsmProfiles.FirstOrDefaultAsync(p => string.Equals(p.DsmName, dsmName, StringComparison.OrdinalIgnoreCase));
            if (profile != null && adj.PendingAdvanceDeduction > 0)
            {
                profile.PendingAdvance += adj.PendingAdvanceDeduction;
                _dbContext.Entry(profile).State = EntityState.Modified;
            }

            _dbContext.DsmSalaryAdjustments.Remove(adj);
            await _dbContext.SaveChangesAsync();
        }
    }
}
