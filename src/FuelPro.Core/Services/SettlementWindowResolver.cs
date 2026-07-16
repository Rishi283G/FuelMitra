using System;
using System.Collections.Generic;
using System.Linq;
using FuelPro.Core.Models;
using Serilog;

namespace FuelPro.Core.Services;

public class ClassifiedRepayment
{
    public CreditorRepayment Repayment { get; set; } = null!;
    public string? SettlementWindow { get; set; } // "Morning", "Day", "Night", or null if unclassifiable
    public DateTime BusinessDate { get; set; }
    public bool IsValid => SettlementWindow != null;
}

public static class SettlementWindowResolver
{
    private static readonly ILogger Logger = Log.ForContext(typeof(SettlementWindowResolver));

    public static ClassifiedRepayment Classify(CreditorRepayment r)
    {
        var result = new ClassifiedRepayment
        {
            Repayment = r,
            BusinessDate = r.RepaymentDate.Date
        };

        string? shift = r.ShiftNumber?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(shift))
        {
            Logger.Warning("Repayment ID {Id} has no Shift Number assigned. Cannot classify settlement window.", r.CreditorRepaymentId);
            result.SettlementWindow = null;
            return result;
        }

        if (shift == "B")
        {
            // Shift B is strictly Day shift (8:00 AM - 8:00 PM)
            result.SettlementWindow = "Day";
            return result;
        }

        if (shift == "A")
        {
            // Shift A spans Morning (12 AM - 8 AM) and Night (8 PM - 12 AM)
            // Check if RepaymentDate has a specific time component (i.e. not midnight)
            if (r.RepaymentDate.TimeOfDay != TimeSpan.Zero)
            {
                int hour = r.RepaymentDate.Hour;
                if (hour >= 20)
                {
                    result.SettlementWindow = "Night";
                }
                else if (hour < 8)
                {
                    result.SettlementWindow = "Morning";
                }
                else
                {
                    Logger.Warning("Repayment ID {Id} is assigned to Shift A but has an unexpected day time component {Time}. Cannot classify.", r.CreditorRepaymentId, r.RepaymentDate.TimeOfDay);
                    result.SettlementWindow = null;
                }
            }
            // Fall back to CreatedAt if it has a non-midnight time component
            else if (r.CreatedAt != default && r.CreatedAt.TimeOfDay != TimeSpan.Zero)
            {
                int hour = r.CreatedAt.Hour;
                if (hour >= 20)
                {
                    result.SettlementWindow = "Night";
                }
                else if (hour < 8)
                {
                    result.SettlementWindow = "Morning";
                }
                else
                {
                    Logger.Warning("Repayment ID {Id} is Shift A but CreatedAt has day time component {Time}. Cannot classify.", r.CreditorRepaymentId, r.CreatedAt.TimeOfDay);
                    result.SettlementWindow = null;
                }
            }
            else
            {
                // No time component on RepaymentDate or CreatedAt.
                // According to Amendment 3, we must never silently guess.
                Logger.Warning("Repayment ID {Id} is assigned to Shift A but lacks a time component. Cannot classify.", r.CreditorRepaymentId);
                result.SettlementWindow = null;
            }
            return result;
        }

        Logger.Warning("Repayment ID {Id} has an unknown Shift Number '{Shift}'. Cannot classify.", r.CreditorRepaymentId, shift);
        result.SettlementWindow = null;
        return result;
    }

    public static List<ClassifiedRepayment> ClassifyRepayments(IEnumerable<CreditorRepayment> repayments)
    {
        return repayments.Select(Classify).ToList();
    }

    public static DateTime GetRepaymentDateTime(DateTime shiftDate, string shiftType, DateTime? now = null)
    {
        var current = now ?? DateTime.Now;
        if (string.Equals(shiftType, "B", StringComparison.OrdinalIgnoreCase))
        {
            // Shift B: 12:00 PM
            return shiftDate.Date.AddHours(12);
        }
        else
        {
            // Shift A: spans 8:00 PM (D-1) to 8:00 AM (D)
            // If logging in real-time, determine Night vs Morning based on current hour:
            if (current.Hour >= 20 || current.Hour < 8)
            {
                if (current.Hour >= 20)
                {
                    // Night slot: 9:00 PM today (which belongs to tomorrow's Shift A)
                    return shiftDate.Date.AddDays(-1).AddHours(21);
                }
                else
                {
                    // Morning slot: 4:00 AM today (which belongs to today's Shift A)
                    return shiftDate.Date.AddHours(4);
                }
            }
            
            // Default when logging backdated or outside shift hours:
            // Since we can't know, default to Morning (4:00 AM) of shiftDate
            return shiftDate.Date.AddHours(4);
        }
    }
}
