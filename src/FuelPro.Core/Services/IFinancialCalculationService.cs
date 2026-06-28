using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FuelPro.Core.Services;

public class FuelProfitDetail
{
    public double HsdLitres { get; set; }
    public double HsdMargin { get; set; }
    public double HsdProfit { get; set; }

    public double MsILitres { get; set; }
    public double MsIMargin { get; set; }
    public double MsIProfit { get; set; }

    public double MsIILitres { get; set; }
    public double MsIIMargin { get; set; }
    public double MsIIProfit { get; set; }

    public double CngLitres { get; set; }
    public double CngMargin { get; set; }
    public double CngProfit { get; set; }

    public double TotalLitres => HsdLitres + MsILitres + MsIILitres + CngLitres;
    public double TotalFuelProfit => HsdProfit + MsIProfit + MsIIProfit + CngProfit;
}

public class ProductProfitDetail
{
    public string ProductType { get; set; } = "Oil"; // "Oil" or "DEF"
    public double OpeningStock { get; set; }
    public double ClosingStock { get; set; }
    public double PurchasesQuantity { get; set; }
    public double PurchasesCost { get; set; }
    public double AveragePurchasePrice { get; set; }
    public double SalesQuantity { get; set; }
    public double SalePrice { get; set; }
    public double SalesRevenue { get; set; }
    public double CostOfGoodsSold { get; set; }
    public double TotalProfit { get; set; }
}

public class FinancialCalculationResult
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public FuelProfitDetail FuelProfit { get; set; } = new();
    public ProductProfitDetail OilProfit { get; set; } = new();
    public ProductProfitDetail DefProfit { get; set; } = new();

    public double GrossProfit => FuelProfit.TotalFuelProfit + OilProfit.TotalProfit + DefProfit.TotalProfit;

    public double TotalExpenses { get; set; }
    public double ManagerExpenses { get; set; }
    public double TotalDsmSalaries { get; set; }

    public double DsmBaseSalaries { get; set; }
    public double SalaryAdjustments { get; set; }
    public double ShortRecoveries { get; set; }
    public double OwnerOuterExpenses { get; set; }
    public double TotalMismatch { get; set; }

    // Pump Expenses (Owner added - local only)
    public double PumpRent { get; set; }
    public double PumpSalary { get; set; }
    public double PumpTripSheetLoss { get; set; }
    public double PumpDsmShort { get; set; }
    public double PumpBankingExpenses { get; set; }
    public double PumpBpclPortalExpenses { get; set; }
    public double PumpFuelAndTravel { get; set; }
    public double PumpOilPurchase { get; set; }
    public double PumpRepairsAndMaintenance { get; set; }
    public double PumpElectricity { get; set; }
    public double PumpOfficeExpenses { get; set; }
    public double PumpPrintingExpense { get; set; }
    public double PumpOtherAmount { get; set; }
    public double TotalPumpExpenses => PumpRent + PumpSalary + PumpTripSheetLoss + PumpDsmShort + PumpBankingExpenses + PumpBpclPortalExpenses + PumpFuelAndTravel + PumpOilPurchase + PumpRepairsAndMaintenance + PumpElectricity + PumpOfficeExpenses + PumpPrintingExpense + PumpOtherAmount;

    // Net profit subtracts all expenses (both PWA outer expenses and local detailed pump expenses) and includes total mismatch (excess/shortage)
    public double NetProfit => GrossProfit - TotalExpenses - DsmBaseSalaries - SalaryAdjustments + ShortRecoveries - OwnerOuterExpenses - TotalPumpExpenses + TotalMismatch;
}

public class DsmSalaryRowDto
{
    public string DsmName { get; set; } = string.Empty;
    public string SalaryType { get; set; } = "FixedMonthly"; // "FixedMonthly", "PerShift", "PerDay"
    public double BaseSalary { get; set; }
    public DateTime? JoiningDate { get; set; }
    public int ShiftsWorked { get; set; }
    public int UniqueDaysWorked { get; set; }
    public double EarnedBase { get; set; }
    public double ShortRecovery { get; set; }
    public double AdvancePaid { get; set; }
    public double OtherAdjustments { get; set; }
    public double PersonalDebtorDeduction { get; set; }
    public double PendingAdvanceDeduction { get; set; }
    public double RemainingPendingAdvance { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public double NetSalary { get; set; }
}

public class OilDefStockReportDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    
    public double OilOpening { get; set; }
    public double OilClosing { get; set; }
    public double OilPurchasesQty { get; set; }
    public double OilPurchasesCost { get; set; }
    public double OilAvgPurchasePrice { get; set; }
    public double OilSalesQty { get; set; }
    public double OilSalePrice { get; set; }
    public double OilProfit { get; set; }

    public double DefOpening { get; set; }
    public double DefClosing { get; set; }
    public double DefPurchasesQty { get; set; }
    public double DefPurchasesCost { get; set; }
    public double DefAvgPurchasePrice { get; set; }
    public double DefSalesQty { get; set; }
    public double DefSalePrice { get; set; }
    public double DefProfit { get; set; }

    public double TotalProfit => OilProfit + DefProfit;
}

public interface IFinancialCalculationService
{
    Task<FinancialCalculationResult> CalculateFinancialsAsync(DateTime startDate, DateTime endDate);
    Task<List<DsmSalaryRowDto>> CalculateDsmSalariesAsync(int year, int month);
    Task<OilDefStockReportDto> GenerateStockReportAsync(int year, int month);
    Task SaveDsmSalaryAdjustmentsAsync(int year, int month, List<DsmSalaryRowDto> rows);
    Task DeleteDsmSalaryAdjustmentAsync(int year, int month, string dsmName);
}
