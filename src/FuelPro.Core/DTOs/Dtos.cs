namespace FuelPro.Core.DTOs;

/// <summary>
/// Lightweight DSM entry for list displays.
/// </summary>
public class DsmEntrySummaryDto
{
    public int SequenceNo { get; set; }
    public string SequenceDisplay => SequenceNo > 0 ? $"#{SequenceNo}" : string.Empty;
    public int DsmEntryId { get; set; }
    public string DsmName { get; set; } = string.Empty;
    public int PumpId { get; set; }
    public int? ConnectedPumpId { get; set; }
    public int? ReconciledToPumpId { get; set; }
    public double GrossSales { get; set; }
    public double TotalPaymentIn { get; set; }
    public double TotalCollection { get => TotalPaymentIn; set => TotalPaymentIn = value; }
    public double Difference { get; set; }
    public DateTime CreatedAt { get; set; }

    public string PumpDisplay
    {
        get
        {
            if (ReconciledToPumpId.HasValue)
            {
                return $"Pump {PumpId} (Connected to {ReconciledToPumpId.Value})";
            }
            if (ConnectedPumpId.HasValue)
            {
                return $"Pump {PumpId} + Pump {ConnectedPumpId.Value}";
            }
            return $"Pump {PumpId}";
        }
    }
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
    public double TotalCngLitres { get; set; }
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
    public List<DsmShiftTotalDto> DsmShiftTotals { get; set; } = new();

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
    public double CngLitres { get; set; }
    public double CngRate { get; set; }
    public double CngAmount { get; set; }
    public double TotalLitres { get; set; }
    public double TotalFuelSaleAmount { get; set; }
    public double TotalMsDispensed { get; set; }
    public List<OtherCashRowDto> OtherCashRows { get; set; } = new();
    public double OtherCashTotal { get; set; }
    public double GrandTotalSaleAmount { get; set; } // Fuel + OtherCash

    // TABLE F — Final Reconciliation
    public double MsTesting { get; set; }
    public double HsdTesting { get; set; }
    public double CngTesting { get; set; }
    public double PhonePeTotal { get; set; }
    public double PhonePeMorningTotal { get; set; }
    public double PhonePeNightTotal { get; set; }
    public double PhonePeCardTotal { get; set; }
    public double PhonePeCardMorningTotal { get; set; }
    public double PhonePeCardNightTotal { get; set; }
    public double PetroCardTotal { get; set; }
    public double PetroCardMorningTotal { get; set; }
    public double PetroCardNightTotal { get; set; }
    public List<OilDefSaleDisplayRow> OilDefSales { get; set; } = new();
    public double OilDefSalesTotal { get; set; }
    public double CreditCardTotal { get; set; } // Kept for legacy/combined display if needed
    public double CreditCardMorningTotal { get; set; }
    public double CreditCardNightTotal { get; set; }
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
    public string Shift { get; set; } = string.Empty;
    public string ShiftLabel => Shift;
    public int PumpId { get; set; }
    public int? ConnectedPumpId { get; set; }

    public string PumpLabel
    {
        get
        {
            if (PumpId <= 0) return "";
            if (ConnectedPumpId.HasValue && ConnectedPumpId.Value > 0)
            {
                return $"Pump {PumpId} & {ConnectedPumpId.Value}";
            }
            return $"Pump {PumpId}";
        }
    }

    public string PumpNoDisplay
    {
        get
        {
            if (PumpId <= 0) return "";
            if (ConnectedPumpId.HasValue && ConnectedPumpId.Value > 0)
            {
                return $"{PumpId} & {ConnectedPumpId.Value}";
            }
            return $"{PumpId}";
        }
    }

    public double PhonePeCard { get; set; }
    public double PhonePeCardMorning { get; set; }
    public double PhonePeCardDay { get; set; }
    public double PhonePeCardNight { get; set; }
    public double PhonePe { get; set; }
    public double PhonePeMorning { get; set; }
    public double PhonePeDay { get; set; }
    public double PhonePeNight { get; set; }
    public double CreditCardMorning { get; set; }
    public double CreditCardDay { get; set; }
    public double CreditCardNight { get; set; }
    public double PetroCard { get; set; }
    public double PetroCardMorning { get; set; }
    public double PetroCardDay { get; set; }
    public double PetroCardNight { get; set; }
    public double Others { get; set; }

    public double DynamicCollectionsTotal { get; set; }
    public Dictionary<string, double> DynamicCollections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public double SbiRedeem { get; set; }
    public double Paytm { get; set; }
    public double QrPayment { get; set; }
    public double Mobikwik { get; set; }
    public double CashDeposit { get; set; }  // Cash 1 — Bank Deposit
    public double Debit { get; set; }        // Sum of creditors/debit entries
    public double Expenses { get; set; }
    public double Testing { get; set; }      // Testing
    public double CashInHand { get; set; }   // Cash 2
    public double GrossSales { get; set; }

    public double PhonePeTotal => (PhonePeMorning + PhonePeNight + PhonePeDay) > 0 ? (PhonePeMorning + PhonePeNight + PhonePeDay) : PhonePe;
    public double PhonePeCardTotal => (PhonePeCardMorning + PhonePeCardNight + PhonePeCardDay) > 0 ? (PhonePeCardMorning + PhonePeCardNight + PhonePeCardDay) : PhonePeCard;
    public double CreditCardTotal => ((CreditCardMorning + CreditCardNight + CreditCardDay) > 0 ? (CreditCardMorning + CreditCardNight + CreditCardDay) : 0) + PhonePeCardTotal;
    public double PetroCardTotal => (PetroCardMorning + PetroCardNight + PetroCardDay) > 0 ? (PetroCardMorning + PetroCardNight + PetroCardDay) : PetroCard;
    public double BankCash => CashDeposit;
    public double DebtorSales => Debit;
    public double GrossSale => GrossSales;
    public double Difference => (CashDeposit + CashInHand + PhonePeTotal + CreditCardTotal + PetroCardTotal + DynamicCollectionsTotal + Debit + Expenses + Testing) - GrossSales;
    public double Mismatch => Difference;
    public double ShortAmount => Difference;

    public string DynamicBreakdownDisplay
    {
        get
        {
            if (DynamicCollections != null && DynamicCollections.Count > 0)
            {
                var nonZero = DynamicCollections.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key}: ₹{kv.Value:N2}");
                var text = string.Join(", ", nonZero);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            return DynamicCollectionsTotal > 0 ? $"₹{DynamicCollectionsTotal:N2}" : "—";
        }
    }

    public double GetAmount(string codeOrName)
    {
        if (string.IsNullOrWhiteSpace(codeOrName)) return 0;
        var clean = codeOrName.Trim().Replace("_", "").Replace(" ", "");

        if (clean.Equals("PHONEPE", StringComparison.OrdinalIgnoreCase))
            return PhonePeTotal;
        if (clean.Equals("CREDITCARD", StringComparison.OrdinalIgnoreCase) || clean.Equals("PINELABCARD", StringComparison.OrdinalIgnoreCase) || clean.Equals("CARD", StringComparison.OrdinalIgnoreCase))
            return CreditCardTotal;
        if (clean.Equals("PETROCARD", StringComparison.OrdinalIgnoreCase) || clean.Equals("PETRO", StringComparison.OrdinalIgnoreCase))
            return PetroCardTotal;
        if (clean.Equals("CASHDEPOSIT", StringComparison.OrdinalIgnoreCase) || clean.Equals("BANKCASH", StringComparison.OrdinalIgnoreCase))
            return CashDeposit;
        if (clean.Equals("CASHINHAND", StringComparison.OrdinalIgnoreCase) || clean.Equals("HANDCASH", StringComparison.OrdinalIgnoreCase))
            return CashInHand;
        if (clean.Equals("DEBIT", StringComparison.OrdinalIgnoreCase) || clean.Equals("DEBTORS", StringComparison.OrdinalIgnoreCase))
            return Debit;
        if (clean.Equals("EXPENSES", StringComparison.OrdinalIgnoreCase))
            return Expenses;
        if (clean.Equals("TESTING", StringComparison.OrdinalIgnoreCase))
            return Testing;

        if (DynamicCollections != null)
        {
            if (DynamicCollections.TryGetValue(codeOrName, out var amt)) return amt;
            var match = DynamicCollections.FirstOrDefault(kv => string.Equals(kv.Key?.Replace("_", "").Replace(" ", ""), clean, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match.Key)) return match.Value;
        }

        if (clean.Equals("SBIREDEEM", StringComparison.OrdinalIgnoreCase)) return SbiRedeem;
        if (clean.Equals("PAYTM", StringComparison.OrdinalIgnoreCase)) return Paytm;
        if (clean.Equals("QR", StringComparison.OrdinalIgnoreCase) || clean.Equals("QRONLINE", StringComparison.OrdinalIgnoreCase)) return QrPayment;
        if (clean.Equals("MOBIKWIK", StringComparison.OrdinalIgnoreCase)) return Mobikwik;

        return 0;
    }

    public double this[string codeOrName] => GetAmount(codeOrName);
}

/// <summary>
/// Tank-wise testing summary item for reconciliation and display.
/// </summary>
public class TestingSummaryItem
{
    public string TankName { get; set; } = string.Empty;
    public string FuelType { get; set; } = string.Empty;
    public double VolumeLitres { get; set; }
    public double Rate { get; set; }
    public double Amount { get; set; }
    public string DisplayLabel => VolumeLitres > 0 ? $"{TankName} Testing ({VolumeLitres:N2} Ltr)" : $"{TankName} Testing";
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
    public List<CashDenomDisplayRow> ToDisplayRows(bool isCash1 = false)
    {
        var rows = new List<CashDenomDisplayRow>
        {
            new() { Denomination = "₹500", TotalCount = Total500, TotalAmount = Total500 * 500.0 },
            new() { Denomination = "₹200", TotalCount = Total200, TotalAmount = Total200 * 200.0 },
            new() { Denomination = "₹100", TotalCount = Total100, TotalAmount = Total100 * 100.0 }
        };

        if (!isCash1)
        {
            rows.Add(new() { Denomination = "₹50",  TotalCount = Total50,  TotalAmount = Total50 * 50.0 });
            rows.Add(new() { Denomination = "₹20",  TotalCount = Total20,  TotalAmount = Total20 * 20.0 });
            rows.Add(new() { Denomination = "₹10",  TotalCount = Total10,  TotalAmount = Total10 * 10.0 });
            rows.Add(new() { Denomination = "Coin", TotalCount = TotalCoins, TotalAmount = TotalCoins });
        }

        if (CashDepositTotal > 0 && Total500 == 0 && Total200 == 0 && Total100 == 0 && Total50 == 0 && Total20 == 0 && Total10 == 0 && TotalCoins == 0)
        {
            rows.Add(new() { Denomination = "Cash 1", TotalCount = 0, TotalAmount = CashDepositTotal });
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
/// Flat list row for Debit register (Table C).
/// </summary>
public class DebitRegisterRowDto
{
    public string DsmName { get; set; } = string.Empty;
    public int PumpId { get; set; }
    public string DebtorName { get; set; } = string.Empty;
    public string? ChequeNo { get; set; }
    public string? VehicleNumber { get; set; }
    public string? SlipNumber { get; set; }
    public double Amount { get; set; }
}

/// <summary>
/// One row in the DSM Personal Debtors print section.
/// </summary>
public class DsmPersonalDebtorPrintDto
{
    public string DsmName { get; set; } = string.Empty;
    public string FuelProduct { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
    public double Amount { get; set; }
}

public class DsmPersonalDebtorRepaymentPrintDto
{
    public string DsmName { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string RefNo { get; set; } = string.Empty;
    public double Amount { get; set; }
}

public class KhandharePetroleumPrintDto
{
    public string Name { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public string SlipNumber { get; set; } = string.Empty;
    public double Amount { get; set; }
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
    public string DescriptionWithBreakdown => Description;
    public double Amount { get; set; }
}

/// <summary>
/// DTO for tracking outstanding creditor balances on the Dashboard.
/// </summary>
public class CreditorBalanceDto
{
    public string DebtorName { get; set; } = string.Empty;
    public double TotalDebitAmount { get; set; }
}

public class DebtorLogEntryDto
{
    public DateTime Date { get; set; }
    public string DsmName { get; set; } = string.Empty;
    public string DebtorName { get; set; } = string.Empty;
    public string? ChequeNo { get; set; }
    public double Amount { get; set; }
}

/// <summary>
/// Aggregated totals per DSM for a shift across multiple sessions/entries.
/// </summary>
public class DsmShiftTotalDto
{
    public string DsmName { get; set; } = string.Empty;
    public int SessionsCount { get; set; }
    public string AssignedPumpsDisplay { get; set; } = string.Empty;
    public double GrossSales { get; set; }
    public double TotalCollection { get; set; }
    public double CashDeposit { get; set; } // Cash1
    public double CashInHand { get; set; }  // Cash2
    public double PhonePe { get; set; }
    public double PhonePeCard { get; set; }
    public double CreditCard { get; set; }
    public double PetroCard { get; set; }
    public double DynamicCollectionsTotal { get; set; }
    public Dictionary<string, double> DynamicCollections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string DynamicBreakdownDisplay
    {
        get
        {
            if (DynamicCollections != null && DynamicCollections.Count > 0)
            {
                var nonZero = DynamicCollections.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key}: ₹{kv.Value:N2}");
                var text = string.Join(", ", nonZero);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            return DynamicCollectionsTotal > 0 ? $"₹{DynamicCollectionsTotal:N2}" : "—";
        }
    }
    public double DigitalTotal => PhonePe + PhonePeCard + CreditCard + PetroCard + DynamicCollectionsTotal;
    public double Debit { get; set; }
    public double Expenses { get; set; }
    public double Testing { get; set; }
    public double Mismatch { get; set; }

    public double GetAmount(string codeOrName)
    {
        if (string.IsNullOrWhiteSpace(codeOrName)) return 0;
        var clean = codeOrName.Trim().Replace("_", "").Replace(" ", "");

        if (clean.Equals("PHONEPE", StringComparison.OrdinalIgnoreCase))
            return PhonePe;
        if (clean.Equals("CREDITCARD", StringComparison.OrdinalIgnoreCase) || clean.Equals("PINELABCARD", StringComparison.OrdinalIgnoreCase) || clean.Equals("CARD", StringComparison.OrdinalIgnoreCase))
            return CreditCard + PhonePeCard;
        if (clean.Equals("PETROCARD", StringComparison.OrdinalIgnoreCase) || clean.Equals("PETRO", StringComparison.OrdinalIgnoreCase))
            return PetroCard;
        if (clean.Equals("CASHDEPOSIT", StringComparison.OrdinalIgnoreCase) || clean.Equals("BANKCASH", StringComparison.OrdinalIgnoreCase))
            return CashDeposit;
        if (clean.Equals("CASHINHAND", StringComparison.OrdinalIgnoreCase) || clean.Equals("HANDCASH", StringComparison.OrdinalIgnoreCase))
            return CashInHand;
        if (clean.Equals("DEBIT", StringComparison.OrdinalIgnoreCase) || clean.Equals("DEBTORS", StringComparison.OrdinalIgnoreCase))
            return Debit;
        if (clean.Equals("EXPENSES", StringComparison.OrdinalIgnoreCase))
            return Expenses;
        if (clean.Equals("TESTING", StringComparison.OrdinalIgnoreCase))
            return Testing;

        if (DynamicCollections != null)
        {
            if (DynamicCollections.TryGetValue(codeOrName, out var amt)) return amt;
            var match = DynamicCollections.FirstOrDefault(kv => string.Equals(kv.Key?.Replace("_", "").Replace(" ", ""), clean, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match.Key)) return match.Value;
        }

        return 0;
    }

    public double this[string codeOrName] => GetAmount(codeOrName);
}
