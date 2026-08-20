using System;
using System.Collections.Generic;

namespace FuelPro.Core.DTOs;

public class ShiftReportDto
{
    public DateTime Date { get; set; }
    public string DateString { get; set; } = string.Empty;
    public string ShiftLabel { get; set; } = string.Empty; // "I", "II", "III"
    public string StationName { get; set; } = string.Empty;
    public string? ManagerName { get; set; }
    
    // Fuel Summary
    public List<FuelSaleRowDto> FuelSales { get; set; } = new();
    public double TotalFuelLitres { get; set; }
    public double TotalFuelAmount { get; set; }
    public double OtherCashTotal { get; set; }
    public double GrandTotalSaleAmount { get; set; }

    // Tank Summary (AGS)
    public List<NozzleGroupDto> TankSummary { get; set; } = new();
    public List<NozzleGroupDto> NozzleGroups { get; set; } = new();

    // DSM Summary (Table A)
    public List<DsmSummaryRowDto> DsmSummaryRows { get; set; } = new();
    public DsmSummaryRowDto DsmSummaryTotals { get; set; } = new();
    public List<DsmShiftTotalDto> DsmShiftTotals { get; set; } = new();

    // Cash Summary (Table B)
    public CashAggregateDto Cash1 { get; set; } = new();
    public CashAggregateDto Cash2 { get; set; } = new();

    // Debtor Entries (Table C)
    public List<DebitRegisterRowDto> CreditorRows { get; set; } = new();
    public double CreditorsTotal { get; set; }

    // Expenses (Table D)
    public List<ExpenseRegisterRowDto> ExpenseRows { get; set; } = new();
    public double ExpensesTotal { get; set; }

    // Debtor Repayments
    public List<CreditorRepaymentPrintDto> DebtorRepayments { get; set; } = new();
    public double DebtorRepaymentsTotal { get; set; }
    public double CashRepayments { get; set; }
    public double PhonePeRepayments { get; set; }
    public double CreditCardRepayments { get; set; }
    public double PetroCardRepayments { get; set; }
    public double PetroCardMorning { get; set; }
    public double PetroCardNight { get; set; }
    public double BankCashRepayments { get; set; }
    public List<RepaymentBreakdownDto> RepaymentBreakdown { get; set; } = new();
    public List<DsmPersonalDebtorPrintDto> PersonalDebtors { get; set; } = new();
    public List<DsmPersonalDebtorRepaymentPrintDto> PersonalDebtorRepayments { get; set; } = new();
    public List<KhandharePetroleumPrintDto> KhandhareEntries { get; set; } = new();
    public List<DsmQrPaymentPrintDto> QrPayments { get; set; } = new();

    // Oil & DEF Sales (Phase 3/4)
    public List<OilDefSaleDisplayRow> OilDefSales { get; set; } = new();
    public double OilDefSalesTotal { get; set; }

    // Standardized Collection Breakdown (16 categories)
    public List<CollectionCategoryDto> CollectionBreakdown { get; set; } = new();

    // Final Reconciliation
    public double ExpectedCollection { get; set; }
    public double ActualCollection { get; set; }
    public double Difference { get; set; }
    public double TotalDsmShort { get; set; }
    public bool IsBalanced { get; set; }
    public string BalancedStatus { get; set; } = string.Empty;
}

public class DayReportDto
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string DateString { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public string? Shift1Manager { get; set; }
    public string? Shift2Manager { get; set; }
    
    // Fuel Summary
    public List<FuelSaleRowDto> FuelSales { get; set; } = new();
    public double TotalFuelLitres { get; set; }
    public double TotalFuelAmount { get; set; }
    public double OtherCashTotal { get; set; }
    public double GrandTotalSaleAmount { get; set; }

    // Tank Summary (AGS)
    public List<NozzleGroupDto> TankSummary { get; set; } = new();

    // DSM Summary (All shifts of the day)
    public List<DsmSummaryRowDto> DsmSummaryRows { get; set; } = new();
    public DsmSummaryRowDto DsmSummaryTotals { get; set; } = new();
    public List<DsmShiftTotalDto> DsmShiftTotals { get; set; } = new();

    // Cash Summary
    public CashAggregateDto Cash1 { get; set; } = new();
    public CashAggregateDto Cash2 { get; set; } = new();

    // Debtors (Table C)
    public List<DebitRegisterRowDto> CreditorRows { get; set; } = new();
    public double CreditorsTotal { get; set; }

    // Expenses (Table D)
    public List<ExpenseRegisterRowDto> ExpenseRows { get; set; } = new();
    public double ExpensesTotal { get; set; }

    // Debtor Repayments
    public List<CreditorRepaymentPrintDto> DebtorRepayments { get; set; } = new();
    public double DebtorRepaymentsTotal { get; set; }
    public double CashRepayments { get; set; }
    public double PhonePeRepayments { get; set; }
    public double CreditCardRepayments { get; set; }
    public double PetroCardRepayments { get; set; }
    public double PetroCardMorning { get; set; }
    public double PetroCardNight { get; set; }
    public double BankCashRepayments { get; set; }
    public List<RepaymentBreakdownDto> RepaymentBreakdown { get; set; } = new();
    public List<DsmPersonalDebtorPrintDto> PersonalDebtors { get; set; } = new();
    public List<DsmPersonalDebtorRepaymentPrintDto> PersonalDebtorRepayments { get; set; } = new();
    public List<KhandharePetroleumPrintDto> KhandhareEntries { get; set; } = new();
    public List<DsmQrPaymentPrintDto> QrPayments { get; set; } = new();

    // Oil & DEF Sales (Phase 3/4)
    public List<OilDefSaleDisplayRow> OilDefSales { get; set; } = new();
    public double OilDefSalesTotal { get; set; }

    // Standardized Collection Breakdown (16 categories)
    public List<CollectionCategoryDto> CollectionBreakdown { get; set; } = new();

    // Final Reconciliation
    public double ExpectedCollection { get; set; }
    public double ActualCollection { get; set; }
    public double Difference { get; set; }
    public double TotalDsmShort { get; set; }
    public bool IsBalanced { get; set; }
    public string BalancedStatus { get; set; } = string.Empty;
}

public class CollectionCategoryDto
{
    public string Category { get; set; } = string.Empty;
    public double Amount { get; set; }
    public double BaseAmount { get; set; }
    public double RecoveryAmount { get; set; }
    public double Volume { get; set; }

    public string DescriptionWithBreakdown =>
        Volume > 0
            ? $"{Category} ({Volume:N2} Ltr)"
            : (RecoveryAmount > 0 
                ? $"{Category} (Fuel: ₹{BaseAmount:N2} | Recovery: ₹{RecoveryAmount:N2})"
                : Category);
}

public class RepaymentBreakdownDto
{
    public string PaymentMethod { get; set; } = string.Empty;
    public double Amount { get; set; }
    public bool IsReconcilable { get; set; }
}

public class FuelSaleRowDto
{
    public string Description { get; set; } = string.Empty;
    public string FuelType { get; set; } = string.Empty;
    public double Litres { get; set; }
    public double Rate { get; set; }
    public double Amount { get; set; }
}

public class OilDefSaleDisplayRow
{
    public string ProductName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public double Quantity { get; set; }
    public double Rate { get; set; }
    public double Total { get; set; }
}

public class DsmQrPaymentPrintDto
{
    public string DsmName { get; set; } = string.Empty;
    public string TargetDsmName { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string Tid { get; set; } = string.Empty;
    public string Batch { get; set; } = string.Empty;
    public string Slot { get; set; } = string.Empty;
}
