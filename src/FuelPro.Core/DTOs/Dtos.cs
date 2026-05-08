namespace FuelPro.Core.DTOs;

/// <summary>
/// Lightweight DSM entry for list displays.
/// </summary>
public class DsmEntrySummaryDto
{
    public int DsmEntryId { get; set; }
    public string DsmName { get; set; } = string.Empty;
    public int PumpId { get; set; }
    public double GrossSales { get; set; }
    public double TotalPaymentIn { get; set; }
    public double Difference { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Dashboard summary for today's/last shift.
/// </summary>
public class ShiftSummaryDto
{
    public DateTime ShiftDate { get; set; }
    public string ShiftType { get; set; } = "A";
    public int TotalDsmEntries { get; set; }
    public double TotalHsdLitres { get; set; }
    public double TotalMsILitres { get; set; }
    public double TotalMsIILitres { get; set; }
    public double TotalFuelSale { get; set; }
    public double TotalCash { get; set; }
    public double TotalDigitalPayments { get; set; }
    public double TotalDebit { get; set; }
    public double TotalExpenses { get; set; }
    public bool IsLocked { get; set; }
}

/// <summary>
/// Nozzle configuration DTO for UI display.
/// </summary>
public class NozzleConfigDto
{
    public int NozzleNumber { get; set; }
    public string FuelType { get; set; } = string.Empty;
    public double OpeningReading { get; set; }
    public double ClosingReading { get; set; }
    public double SaleLitres { get; set; }
    public double Rate { get; set; }
    public double Amount { get; set; }
}

// ====================================================================
// FINAL CALCULATION DTOs (Tables A through F)
// ====================================================================

/// <summary>
/// Complete final calculation data for a shift.
/// </summary>
public class FinalCalculationDto
{
    public int ShiftId { get; set; }
    public DateTime ShiftDate { get; set; }
    public string ShiftType { get; set; } = "A";
    public bool IsLocked { get; set; }

    // TABLE A — DSM Summary
    public List<DsmSummaryRowDto> DsmSummaryRows { get; set; } = new();

    // TABLE B — Cash Totals
    public CashAggregateDto Cash1Aggregate { get; set; } = new();
    public CashAggregateDto Cash2Aggregate { get; set; } = new();

    // TABLE C — Debit/Creditor Register
    public List<DebitRegisterRowDto> DebitRegisterRows { get; set; } = new();
    public double TotalDebit { get; set; }

    // TABLE D — Expenses Register
    public List<ExpenseRegisterRowDto> ExpenseRegisterRows { get; set; } = new();
    public double TotalExpenses { get; set; }

    // TABLE E — Fuel Dispensed
    public double HsdLitres { get; set; }
    public double HsdRate { get; set; }
    public double HsdAmount { get; set; }
    public double MsILitres { get; set; }
    public double MsIRate { get; set; }
    public double MsIAmount { get; set; }
    public double MsIILitres { get; set; }
    public double MsIIRate { get; set; }
    public double MsIIAmount { get; set; }
    public double TotalLitres { get; set; }
    public double TotalFuelSaleAmount { get; set; }
    public double TotalMsDispensed { get; set; }
    public List<OtherCashRowDto> OtherCashRows { get; set; } = new();
    public double OtherCashTotal { get; set; }
    public double GrandTotalSaleAmount { get; set; } // Fuel + OtherCash

    // TABLE F — Final Reconciliation
    public double MsTesting { get; set; }
    public double HsdTesting { get; set; }
    public double PhonePeTotal { get; set; }
    public double PhonePeMorningTotal { get; set; }
    public double PhonePeNightTotal { get; set; }
    public double PhonePeCardTotal { get; set; }
    public double PhonePeCardMorningTotal { get; set; }
    public double PhonePeCardNightTotal { get; set; }
    public double PetroCardTotal { get; set; }
    public double CreditCardTotal { get; set; }
    public double BankCash { get; set; }
    public double CashInHand { get; set; }
    public double TotalAmounts { get; set; }
    public double ReconciliationDifference { get; set; }
    public bool IncludeOtherCashInGrossSale { get; set; }
}

/// <summary>
/// One row in the DSM Summary table (Table A).
/// </summary>
public class DsmSummaryRowDto
{
    public string DsmName { get; set; } = string.Empty;
    public int PumpId { get; set; }
    public double PhonePeCard { get; set; }
    public double PhonePeCardMorning { get; set; }
    public double PhonePeCardNight { get; set; }
    public double PhonePe { get; set; }
    public double PhonePeMorning { get; set; }
    public double PhonePeNight { get; set; }
    public double CreditCard { get; set; }
    public double PetroCard { get; set; }
    public double CashDeposit { get; set; }  // Cash 1 — Bank Deposit
    public double Debit { get; set; }        // Sum of creditors/debit entries
    public double Expenses { get; set; }
    public double Testing { get; set; }      // HSD Testing + MS Testing
    public double CashInHand { get; set; }   // Cash 2
    public double GrossSales { get; set; }
}

/// <summary>
/// Aggregated cash denomination data (Table B1/B2).
/// </summary>
public class CashAggregateDto
{
    public int Total500 { get; set; }
    public int Total200 { get; set; }
    public int Total100 { get; set; }
    public int Total50 { get; set; }
    public int Total20 { get; set; }
    public int Total10 { get; set; }
    public int TotalCoins { get; set; }
    public double CashDepositTotal { get; set; }
    public double GrandTotal { get; set; }

    /// <summary>
    /// Returns denomination rows for display.
    /// </summary>
    public List<CashDenomDisplayRow> ToDisplayRows()
    {
        var rows = new List<CashDenomDisplayRow>
        {
            new() { Denomination = "₹500", TotalCount = Total500, TotalAmount = Total500 * 500.0 },
            new() { Denomination = "₹200", TotalCount = Total200, TotalAmount = Total200 * 200.0 },
            new() { Denomination = "₹100", TotalCount = Total100, TotalAmount = Total100 * 100.0 },
            new() { Denomination = "₹50",  TotalCount = Total50,  TotalAmount = Total50 * 50.0 },
            new() { Denomination = "₹20",  TotalCount = Total20,  TotalAmount = Total20 * 20.0 },
            new() { Denomination = "₹10",  TotalCount = Total10,  TotalAmount = Total10 * 10.0 },
            new() { Denomination = "Coin", TotalCount = TotalCoins, TotalAmount = TotalCoins }
        };

        if (CashDepositTotal > 0)
        {
            rows.Add(new() { Denomination = "Cash Deposit", TotalCount = 0, TotalAmount = CashDepositTotal });
        }

        return rows;
    }
}

/// <summary>
/// Single denomination row for display in cash tables.
/// </summary>
public class CashDenomDisplayRow
{
    public string Denomination { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public double TotalAmount { get; set; }
}

/// <summary>
/// One row in the Creditor/Debit register (Table C).
/// </summary>
public class DebitRegisterRowDto
{
    public string DsmName { get; set; } = string.Empty;
    public int PumpId { get; set; }
    public string DebtorName { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string? ChequeNo { get; set; }
}

/// <summary>
/// One row in the Expense register (Table D).
/// </summary>
public class ExpenseRegisterRowDto
{
    public int ExpenseId { get; set; }
    public string DsmName { get; set; } = string.Empty;
    public int PumpId { get; set; }
    public string Description { get; set; } = string.Empty;
    public double Amount { get; set; }
    public bool IsShiftLevel { get; set; }
    public bool CanDelete { get; set; }
}

/// <summary>
/// Other cash / cheque row in Table E.
/// </summary>
public class OtherCashRowDto
{
    public int ShiftOtherCashId { get; set; }
    public string Description { get; set; } = string.Empty;
    public double Amount { get; set; }
    public bool IsEditable { get; set; } = true;
}

/// <summary>
/// Reconciliation line item (Table F).
/// </summary>
public class ReconciliationRowDto
{
    public string Description { get; set; } = string.Empty;
    public double Amount { get; set; }
}

/// <summary>
/// DTO for tracking outstanding creditor balances on the Dashboard.
/// </summary>
public class CreditorBalanceDto
{
    public string CreditorName { get; set; } = string.Empty;
    public double TotalDebitAmount { get; set; }
}
