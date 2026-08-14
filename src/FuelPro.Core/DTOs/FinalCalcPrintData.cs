namespace FuelPro.Core.DTOs;

/// <summary>
/// Root print data object serialized to JSON and injected into the HTML template.
/// </summary>
public class FinalCalcPrintData
{
    public string Date { get; set; } = string.Empty;       // "20/04/2026"
    public string ShiftLabel { get; set; } = string.Empty; // "I", "II", "III"
    public string StationName { get; set; } = string.Empty;

    public List<DsmPrintRow> DsmEntries { get; set; } = new();
    public CashPrintBlock Cash1 { get; set; } = new();
    public CashPrintBlock Cash2 { get; set; } = new();
    public List<CreditorPrintRow> Creditors { get; set; } = new();
    public List<FuelSalePrintRow> FuelSale { get; set; } = new();
    public decimal OtherCash { get; set; }
    public decimal Cheque { get; set; }
    public decimal MsILitres { get; set; }
    public decimal MsIILitres { get; set; }
    public decimal CngLitres { get; set; }
    public decimal GrossFuelSaleTotal { get; set; }
    public List<ExpensePrintRow> Expenses { get; set; } = new();
    public ReconciliationPrintBlock Reconciliation { get; set; } = new();
    public List<NozzleGroupDto> NozzleGroups { get; set; } = new();
    public List<CreditorRepaymentPrintDto> SameDayRepayments { get; set; } = new();
}

public class CreditorRepaymentPrintDto
{
    public string DebtorName { get; set; } = string.Empty;
    public string PaymentMode { get; set; } = string.Empty;
    public string RefNo { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class DsmPrintRow
{
    public string DsmName { get; set; } = string.Empty;
    public string PumpNo { get; set; } = string.Empty;
    public decimal CardAmount { get; set; }
    public decimal CreditCardMorning { get; set; }
    public decimal CreditCardNight { get; set; }
    public decimal PhonePay { get; set; }
    public decimal PhonePeMorning { get; set; }
    public decimal PhonePeNight { get; set; }
    public decimal PhonePeCardMorning { get; set; }
    public decimal PhonePeCardNight { get; set; }
    public decimal PetroCard { get; set; }
    public decimal PetroCardMorning { get; set; }
    public decimal PetroCardNight { get; set; }
    public decimal Debit { get; set; }
    public decimal Expenses { get; set; }
    public decimal Testing { get; set; }
    public decimal Cash1 { get; set; }
    public decimal Cash2 { get; set; }
    public decimal GrossSale { get; set; }
}

public class CashPrintBlock
{
    public int Count500 { get; set; }
    public decimal Amt500 { get; set; }
    public int Count200 { get; set; }
    public decimal Amt200 { get; set; }
    public int Count100 { get; set; }
    public decimal Amt100 { get; set; }
    public int Count50 { get; set; }
    public decimal Amt50 { get; set; }
    public int Count20 { get; set; }
    public decimal Amt20 { get; set; }
    public int Count10 { get; set; }
    public decimal Amt10 { get; set; }
    public decimal CoinAmt { get; set; }
    public decimal Total { get; set; }
}

public class CreditorPrintRow
{
    public string Name { get; set; } = string.Empty;
    public string SlipNo { get; set; } = string.Empty;
    public string ChequeNo { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Note { get; set; } = string.Empty; // e.g. "Mixing", "Short"
}

public class FuelSalePrintRow
{
    public string Description { get; set; } = string.Empty;
    public string FuelType { get; set; } = string.Empty;  // "HSD", "MS-I", "MS-II"
    public string TankLabel { get; set; } = "20 KL";
    public decimal Litres { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
}

public class ExpensePrintRow
{
    public string DsmName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class ReconciliationPrintBlock
{
    public decimal MsTesting { get; set; }
    public decimal HsdTesting { get; set; }
    public decimal HsdTesting2 { get; set; }
    public decimal CngTesting { get; set; }
    public decimal PhonePe { get; set; }
    public decimal PhonePeCardMorning { get; set; }
    public decimal PhonePeCardNight { get; set; }
    public decimal PhonePeMorning { get; set; }
    public decimal PhonePeNight { get; set; }
    public decimal PetroCard { get; set; }
    public decimal Debit { get; set; }
    public decimal CreditCard { get; set; }
    public decimal CreditCardMorning { get; set; }
    public decimal CreditCardNight { get; set; }
    public decimal BankCash { get; set; }
    public decimal CashInHand { get; set; }
    public decimal Expenses { get; set; }
    public decimal Total { get; set; }
    public decimal TotalDsmShort { get; set; }
}
