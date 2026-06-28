using System;
using System.Collections.Generic;

namespace FuelPro.Core.DTOs;

public class DebtorLedgerPrintData
{
    public string DebtorName { get; set; } = string.Empty;
    public string DebtorPhone { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public double OpeningBalance { get; set; }
    public double TotalDebt { get; set; }
    public double TotalRepayments { get; set; }
    public double ClosingBalance { get; set; }
    public List<DebtorLedgerPrintRow> Transactions { get; set; } = new();
}

public class DebtorLedgerPrintRow
{
    public string Date { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Debit { get; set; }
    public double Credit { get; set; }
    public double RunningBalance { get; set; }
}
