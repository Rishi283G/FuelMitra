namespace FuelPro.Core.DTOs;

/// <summary>
/// Root data object serialized to JSON and injected into DsmSheetPrintTemplate.html.
/// Contains all 9 sections required for the per-DSM print sheet.
/// </summary>
public class DsmSheetPrintData
{
    // Header
    public string StationName   { get; set; } = string.Empty;
    public string CompanyName   { get; set; } = "VKD Petroleum";
    public string Date          { get; set; } = string.Empty; // "10/06/2026"
    public string Shift         { get; set; } = string.Empty; // "A", "B", "C"
    public string DsmName       { get; set; } = string.Empty;
    public string PumpNo        { get; set; } = string.Empty;
    public string PrintedAt     { get; set; } = string.Empty; // timestamp
    public string StartTime     { get; set; } = string.Empty;
    public string EndTime       { get; set; } = string.Empty;

    // Section 2 — Nozzle Readings
    public List<DsmNozzlePrintRow> NozzleRows { get; set; } = new();
    public double TotalLitres   { get; set; }
    public double GrossSales    { get; set; }

    // Section 3 — Payment Collection
    public DsmPaymentPrintBlock Payments { get; set; } = new();

    // Section 4 — Cash Denomination Summary
    public DsmCashDenomPrintBlock CashDenom { get; set; } = new();

    // Section 5 — Creditors
    public List<DsmCreditorPrintRow> Creditors { get; set; } = new();
    public double TotalCreditors { get; set; }

    // Section 6 — Expenses
    public List<DsmExpensePrintRow> Expenses { get; set; } = new();
    public double TotalExpenses  { get; set; }

    // Section 7 — Testing
    public List<DsmTestingPrintRow> Testing { get; set; } = new();
    public double TotalTesting   { get; set; }

    // Section 8 — Reconciliation
    public DsmReconciliationPrintBlock Reconciliation { get; set; } = new();

    // Section 9 — Personal Debtors
    public List<DsmPersonalDebtorPrintRow> PersonalDebtors { get; set; } = new();
    public double TotalPersonalDebtors { get; set; }
}

public class DsmPersonalDebtorPrintRow
{
    public string FuelProduct { get; set; } = string.Empty;
    public string Remarks     { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public double Amount      { get; set; }
    public string CardTid     { get; set; } = string.Empty;
    public string CardBatch   { get; set; } = string.Empty;
}

public class DsmNozzlePrintRow
{
    public int    NozzleNumber  { get; set; }
    public string FuelType      { get; set; } = string.Empty;
    public double OpeningReading { get; set; }
    public double ClosingReading { get; set; }
    public double SaleLitres    { get; set; }
    public double Rate          { get; set; }
    public double Amount        { get; set; }
}

public class DsmPaymentPrintBlock
{
    public double PhonePeCardMorning  { get; set; }
    public double PhonePeCardNight    { get; set; }
    public double PhonePeMorning      { get; set; }
    public double PhonePeNight        { get; set; }
    public double CreditCardMorning   { get; set; } // PineLab Morning
    public double CreditCardNight     { get; set; } // PineLab Night
    public double PetroCard           { get; set; }
    public double CashDeposit         { get; set; }
    public double Others              { get; set; }
    public double BankDeposit         { get; set; }
    public double TotalDigital        { get; set; }

    public string CardTid             { get; set; } = string.Empty;
    public string CardBatch           { get; set; } = string.Empty;
    public string PhonePeTid          { get; set; } = string.Empty;
    public string PhonePeBatch        { get; set; } = string.Empty;
    public string PetroCardTid        { get; set; } = string.Empty;
    public string PetroCardBatch      { get; set; } = string.Empty;
}

/// <summary>
/// Denomination breakdown for Cash In Hand (Cash2).
/// </summary>
public class DsmCashDenomPrintBlock
{
    public int    Qty500  { get; set; }
    public double Amt500  { get; set; }
    public int    Qty200  { get; set; }
    public double Amt200  { get; set; }
    public int    Qty100  { get; set; }
    public double Amt100  { get; set; }
    public int    Qty50   { get; set; }
    public double Amt50   { get; set; }
    public int    Qty20   { get; set; }
    public double Amt20   { get; set; }
    public int    Qty10   { get; set; }
    public double Amt10   { get; set; }
    public double Coins   { get; set; } // Direct rupee amount
    public double Total   { get; set; }
}

public class DsmCreditorPrintRow
{
    public string DebtorName { get; set; } = string.Empty;
    public string ChequeNo   { get; set; } = string.Empty;
    public double Amount     { get; set; }
}

public class DsmExpensePrintRow
{
    public string Description { get; set; } = string.Empty;
    public double Amount      { get; set; }
}

public class DsmTestingPrintRow
{
    public string FuelType { get; set; } = string.Empty;
    public double Litres   { get; set; }
    public double Rate     { get; set; }
    public double Amount   { get; set; }
}

public class DsmReconciliationPrintBlock
{
    public double GrossSales     { get; set; }
    public double TotalCollection { get; set; }
    public double Creditors      { get; set; }
    public double Testing        { get; set; }
    public double Expenses       { get; set; }
    public double Cash           { get; set; }
    public double Mismatch       { get; set; }
}
