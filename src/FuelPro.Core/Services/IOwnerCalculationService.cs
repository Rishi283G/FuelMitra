using System;
using System.Collections.Generic;
using FuelPro.Core.Models;

namespace FuelPro.Core.Services;

public interface IOwnerCalculationService
{
    OwnerCalculationResult Calculate(
        IEnumerable<DsmEntry> entries,
        IEnumerable<Expense> shiftExpenses,
        IEnumerable<ShiftOtherCash> otherCash,
        BusinessDayTidSheet? tidSheet = null);

    Dictionary<DateTime, OwnerCalculationResult> CalculateByDay(
        IEnumerable<DsmEntry> entries,
        IEnumerable<Expense> shiftExpenses,
        IEnumerable<ShiftOtherCash> otherCash,
        Dictionary<DateTime, BusinessDayTidSheet>? tidSheets = null);

    Dictionary<string, OwnerCalculationResult> CalculateByDsm(
        IEnumerable<DsmEntry> entries);
}

public class OwnerCalculationResult
{
    public double GrossSales { get; set; }
    public double CashDeposit { get; set; }
    public double CashInHand { get; set; }
    public double PhonePeDirect { get; set; }
    public double PhonePeCard { get; set; }
    public double CreditCard { get; set; }
    public double PetroCard { get; set; }
    public double Debit { get; set; }
    public double Testing { get; set; }
    public double Expenses { get; set; }

    // Derived properties
    public double TotalPhonePe => PhonePeDirect + PhonePeCard;
    public double TotalCash => CashDeposit + CashInHand;
    public double DirectCollection => CashDeposit + CashInHand + PhonePeDirect + PhonePeCard + CreditCard + PetroCard + Debit;
    public double AdjustedCollection => DirectCollection + Testing + Expenses;
    public double Mismatch => AdjustedCollection - GrossSales;

    // Litres
    public double HsdLitres { get; set; }
    public double MsILitres { get; set; }
    public double MsIILitres { get; set; }
    public double CngLitres { get; set; }
    public double TotalLitres => HsdLitres + MsILitres + MsIILitres + CngLitres;
}
