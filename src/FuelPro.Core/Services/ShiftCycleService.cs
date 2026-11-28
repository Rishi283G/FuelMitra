using System;
using System.Collections.Generic;

namespace FuelPro.Core.Services;

/// <summary>
/// Station operational shift ordering mode.
/// </summary>
public enum ShiftCycleMode
{
    MitaliThreeShiftBca,
    StandardThreeShiftAbc,
    StandardTwoShiftAb
}

/// <summary>
/// Implements the definitive 3-shift operational cycle for PyroSync / Mitali Service Station.
/// Calendar day grouping: Shift B -> Shift C -> Shift A.
/// Predecessors: Prev(B,D) = (D-1,A), Prev(C,D) = (D,B), Prev(A,D) = (D,C).
/// Successors:   Next(B,D) = (D,C),   Next(C,D) = (D,A), Next(A,D) = (D+1,B).
/// Opening shift of Date D: Shift B. Closing shift of Date D: Shift A.
/// </summary>
public class ShiftCycleService : IShiftCycleService
{
    private static readonly string[] ThreeShifts = { "B", "C", "A" };
    private static readonly string[] DisplayShifts = { "B", "C", "A" };
    private static readonly string[] ShiftAAliases = { "A", "a", "I", "i", "Shift A", "SHIFT A", "Shift I", "1" };
    private static readonly string[] ShiftBAliases = { "B", "b", "II", "ii", "Shift B", "SHIFT B", "Shift II", "2" };
    private static readonly string[] ShiftCAliases = { "C", "c", "III", "iii", "Shift C", "SHIFT C", "Shift III", "3" };

    public static ShiftCycleMode DetermineCycleMode(string? stationName, bool hasShiftC)
    {
        if (string.IsNullOrWhiteSpace(stationName) ||
            stationName.Contains("Mitali", StringComparison.OrdinalIgnoreCase))
        {
            return ShiftCycleMode.MitaliThreeShiftBca;
        }

        return hasShiftC
            ? ShiftCycleMode.StandardThreeShiftAbc
            : ShiftCycleMode.StandardTwoShiftAb;
    }

    public static IReadOnlyList<string> GetShiftTypeAliases(string canonicalShiftType)
    {
        return canonicalShiftType switch
        {
            "A" => ShiftAAliases,
            "B" => ShiftBAliases,
            "C" => ShiftCAliases,
            _ => new[] { canonicalShiftType }
        };
    }

    public (DateTime Date, string ShiftType) GetPreviousShift(DateTime date, string shiftType)
    {
        return GetPreviousShift(date, shiftType, ShiftCycleMode.MitaliThreeShiftBca);
    }

    public static (DateTime Date, string ShiftType) GetPreviousShift(DateTime date, string shiftType, ShiftCycleMode mode)
    {
        var norm = NormalizeShiftTypeStatic(shiftType);
        return mode switch
        {
            ShiftCycleMode.StandardTwoShiftAb => norm switch
            {
                "A" => (date.Date.AddDays(-1), "B"),
                "B" => (date.Date, "A"),
                "C" => (date.Date, "B"),
                _ => (date.Date.AddDays(-1), "B")
            },
            ShiftCycleMode.StandardThreeShiftAbc => norm switch
            {
                "A" => (date.Date.AddDays(-1), "C"),
                "B" => (date.Date, "A"),
                "C" => (date.Date, "B"),
                _ => (date.Date.AddDays(-1), "C")
            },
            _ => norm switch
            {
                "B" => (date.Date.AddDays(-1), "A"),
                "C" => (date.Date, "B"),
                "A" => (date.Date, "C"),
                _ => (date.Date.AddDays(-1), "A")
            }
        };
    }

    public IReadOnlyList<string> GetShiftsForDate(DateTime date)
    {
        return ThreeShifts;
    }

    public IReadOnlyList<string> GetDisplayShifts()
    {
        return DisplayShifts;
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

    public string NormalizeShiftType(string shiftType) => NormalizeShiftTypeStatic(shiftType);

    private static string NormalizeShiftTypeStatic(string shiftType)
    {
        if (string.IsNullOrWhiteSpace(shiftType))
            return "B";

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
