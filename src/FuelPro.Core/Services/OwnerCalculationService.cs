using System;
using System.Collections.Generic;
using System.Linq;
using FuelPro.Core.Common;
using FuelPro.Core.Models;

namespace FuelPro.Core.Services;

public class OwnerCalculationService : IOwnerCalculationService
{
    public OwnerCalculationResult Calculate(
        IEnumerable<DsmEntry> entries,
        IEnumerable<Expense> shiftExpenses,
        IEnumerable<ShiftOtherCash> otherCash,
        BusinessDayTidSheet? tidSheet = null)
    {
        var result = new OwnerCalculationResult();

        double entryExpenses = 0;
        foreach (var entry in entries)
        {
            var cash1 = entry.CashDenominations.Where(x => x.CashType == "Cash1").Sum(x => x.TotalAmount);
            var cash2 = entry.CashDenominations.Where(x => x.CashType == "Cash2").Sum(x => x.TotalAmount);

            result.CashDeposit += cash1 > 0 ? cash1 : (entry.PaymentCollection?.CashDeposit ?? 0);
            result.CashInHand += cash2;
            result.PhonePeDirect += (entry.PaymentCollection?.PhonePeMorning ?? 0)
                                    + (entry.PaymentCollection?.PhonePeDay ?? 0)
                                    + (entry.PaymentCollection?.PhonePeNight ?? 0);
            result.PhonePeCard += (entry.PaymentCollection?.PhonePeCardMorning ?? 0)
                                  + (entry.PaymentCollection?.PhonePeCardDay ?? 0)
                                  + (entry.PaymentCollection?.PhonePeCardNight ?? 0);
            result.CreditCard += (entry.PaymentCollection?.CreditCardMorning ?? 0)
                                 + (entry.PaymentCollection?.CreditCardDay ?? 0)
                                 + (entry.PaymentCollection?.CreditCardNight ?? 0);
            result.PetroCard += (entry.PaymentCollection?.PetroCardMorning ?? 0)
                                + (entry.PaymentCollection?.PetroCardDay ?? 0)
                                + (entry.PaymentCollection?.PetroCardNight ?? 0);
            result.DynamicCollection += entry.PaymentCollection?.DynamicItemsTotal ?? 0;
            result.Debit += entry.DebitEntries.Sum(d => d.Amount);
            result.Testing += entry.TestingEntries.Sum(t => t.Amount);
            entryExpenses += entry.Expenses.Sum(e => e.Amount);

            // Gross sales from nozzle readings
            result.GrossSales += entry.NozzleReadings.Sum(r => r.Amount);

            // Litres breakdown
            foreach (var nr in entry.NozzleReadings)
            {
                var ft = PumpConfiguration.GetFuelTypeDisplayName(entry.PumpId, nr.NozzleNumber, entry.Shift?.ShiftDate);
                if (ft == "HSD") result.HsdLitres += nr.SaleLitres;
                else if (ft == "MS-I") result.MsILitres += nr.SaleLitres;
                else if (ft == "MS-II") result.MsIILitres += nr.SaleLitres;
                else if (string.Equals(ft, "CNG", StringComparison.OrdinalIgnoreCase) || (ft != null && ft.Contains("CNG", StringComparison.OrdinalIgnoreCase))) result.CngLitres += nr.SaleLitres;
            }
        }

        // Add date-level/shift-level adjustments
        if (otherCash != null)
        {
            result.GrossSales += otherCash.Sum(o => o.Amount);
        }

        double totalShiftExp = shiftExpenses != null ? shiftExpenses.Sum(e => e.Amount) : 0;
        result.Expenses = entryExpenses + totalShiftExp;

        return result;
    }

    public Dictionary<DateTime, OwnerCalculationResult> CalculateByDay(
        IEnumerable<DsmEntry> entries,
        IEnumerable<Expense> shiftExpenses,
        IEnumerable<ShiftOtherCash> otherCash,
        Dictionary<DateTime, BusinessDayTidSheet>? tidSheets = null)
    {
        var byDay = entries.GroupBy(e =>
        {
            var shift = e.Shift;
            return shift != null ? shift.ShiftDate.Date : DateTime.Today;
        });

        var results = new Dictionary<DateTime, OwnerCalculationResult>();

        foreach (var dayGroup in byDay)
        {
            var date = dayGroup.Key;
            var dayShiftIds = dayGroup.Select(e => e.ShiftId).Distinct().ToList();

            var dateShiftExpenses = shiftExpenses?.Where(e => e.ShiftId.HasValue && dayShiftIds.Contains(e.ShiftId.Value))
                                     ?? Enumerable.Empty<Expense>();
            
            var dateOtherCash = otherCash?.Where(o => o.ShiftDate.Date == date)
                                ?? Enumerable.Empty<ShiftOtherCash>();

            BusinessDayTidSheet? tidSheet = null;
            tidSheets?.TryGetValue(date, out tidSheet);

            var dayResult = Calculate(dayGroup, dateShiftExpenses, dateOtherCash, tidSheet);
            results[date] = dayResult;
        }

        return results;
    }

    public Dictionary<string, OwnerCalculationResult> CalculateByDsm(
        IEnumerable<DsmEntry> entries)
    {
        var byDsm = entries.GroupBy(e => e.DsmName ?? "Unknown");
        var results = new Dictionary<string, OwnerCalculationResult>();

        foreach (var dsmGroup in byDsm)
        {
            var dsmResult = Calculate(dsmGroup, Enumerable.Empty<Expense>(), Enumerable.Empty<ShiftOtherCash>());
            results[dsmGroup.Key] = dsmResult;
        }

        return results;
    }
}
