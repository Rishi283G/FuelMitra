using System;

namespace FuelPro.Core.Models;

public class PumpExpense
{
    public int Id { get; set; }
    public DateTime ExpenseDate { get; set; }
    public double Rent { get; set; }
    public double Salary { get; set; }
    public double TripSheetLoss { get; set; }
    public double DsmShort { get; set; }
    public double BankingExpenses { get; set; }
    public double BpclPortalExpenses { get; set; }
    public double FuelAndTravel { get; set; }
    public double OilPurchase { get; set; }
    public double RepairsAndMaintenance { get; set; }
    public double ElectricityExpenses { get; set; }
    public double OfficeExpenses { get; set; }
    public double PrintingExpense { get; set; }
    public string OtherDescription { get; set; } = string.Empty;
    public double OtherAmount { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
