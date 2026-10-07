using System;
using System.Collections.Generic;

namespace FuelPro.Core.Services;

/// <summary>
/// Implements the definitive 3-shift operational cycle for Mitali Service Station.
/// Order per calendar day: Shift A -> Shift B -> Shift C.
/// Continuity: C(D-1) -> A(D) -> B(D) -> C(D) -> A(D+1).
/// </summary>
public class ShiftCycleService : IShiftCycleService
{
    private static readonly string[] ThreeShifts = { "A", "B", "C" };
    private static readonly string[] DisplayShifts = { "A", "B", "C" };

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
            "A" => (date.Date.AddDays(-1), "C"),
            "B" => (date.Date, "A"),
            "C" => (date.Date, "B"),
            _ => (date.Date.AddDays(-1), "C")
        };
    }

    public (DateTime Date, string ShiftType) GetNextShift(DateTime date, string shiftType)
    {
        var norm = NormalizeShiftType(shiftType);
        return norm switch
        {
            "A" => (date.Date, "B"),
            "B" => (date.Date, "C"),
            "C" => (date.Date.AddDays(1), "A"),
            _ => (date.Date, "B")
        };
    }

    public string GetOpeningShiftForDate(DateTime date)
    {
        return "A";
    }

    public string GetClosingShiftForDate(DateTime date)
    {
        return "C";
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
