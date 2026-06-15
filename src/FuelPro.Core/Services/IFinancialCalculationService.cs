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

    public double TotalLitres => HsdLitres + MsILitres + MsIILitres;
    public double TotalFuelProfit => HsdProfit + MsIProfit + MsIIProfit;
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
    public double TotalDsmSalaries { get; set; }

    public double DsmBaseSalaries { get; set; }
    public double SalaryAdjustments { get; set; }
    public double ShortRecoveries { get; set; }

    public double NetProfit => GrossProfit - TotalExpenses - DsmBaseSalaries - SalaryAdjustments + ShortRecoveries;
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
}
