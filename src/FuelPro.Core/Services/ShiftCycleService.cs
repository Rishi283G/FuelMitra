using System;
using System.Collections.Generic;

namespace FuelPro.Core.Services;

/// <summary>
/// Implements the definitive 3-shift operational cycle for Mitali Service Station.
/// Order per operational day D: Shift B -> Shift C -> Shift A.
/// Continuity: A(D-1) -> B(D) -> C(D) -> A(D) -> B(D+1).
/// </summary>
public class ShiftCycleService : IShiftCycleService
{
    private static readonly string[] ThreeShifts = { "B", "C", "A" };
    private static readonly string[] DisplayShifts = { "B", "C", "A" };

    public IReadOnlyList<string> GetShiftsForDate(DateTime date)
    {
        return ThreeShifts;
    }

    public IReadOnlyList<string> GetDisplayShifts()
    {
        return DisplayShifts;
    }

    public (DateTime Date, string ShiftType) GetPreviousShift(DateTime date, string shiftType)
    {
        var norm = NormalizeShiftType(shiftType);
        return norm switch
        {
            "B" => (date.Date.AddDays(-1), "A"),
            "C" => (date.Date, "B"),
            "A" => (date.Date, "C"),
            _ => (date.Date.AddDays(-1), "A")
        };
    }

    public (DateTime Date, string ShiftType) GetNextShift(DateTime date, string shiftType)
    {
        var norm = NormalizeShiftType(shiftType);
        return norm switch
        {
            "B" => (date.Date, "C"),
            "C" => (date.Date, "A"),
            "A" => (date.Date.AddDays(1), "B"),
            _ => (date.Date, "C")
        };
    }

    public string GetOpeningShiftForDate(DateTime date)
    {
        return "B";
    }

    public string GetClosingShiftForDate(DateTime date)
    {
        return "A";
    }

    public string NormalizeShiftType(string shiftType)
    {
        if (string.IsNullOrWhiteSpace(shiftType))
            return "A";

        var clean = shiftType.Trim().ToUpperInvariant();
        return clean switch
        {
            "A" or "SHIFT A" or "I" or "SHIFT I" or "1" => "A",
            "B" or "SHIFT B" or "II" or "SHIFT II" or "2" => "B",
            "C" or "SHIFT C" or "III" or "SHIFT III" or "3" => "C",
            _ when clean.EndsWith("A") || clean.StartsWith("A") => "A",
            _ when clean.EndsWith("B") || clean.StartsWith("B") => "B",
            _ when clean.EndsWith("C") || clean.StartsWith("C") => "C",
            _ => clean
        };
    }
}
