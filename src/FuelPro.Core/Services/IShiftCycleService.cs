using System;
using System.Collections.Generic;

namespace FuelPro.Core.Services;

/// <summary>
/// Definitive service managing operational shift sequences and continuity tuples for PyroSync.
/// Calendar day grouping: Shift B -> Shift C -> Shift A.
/// Continuous handover chain: A(D-1) closing -> B(D) opening -> B(D) closing -> C(D) opening -> C(D) closing -> A(D) opening -> A(D) closing -> B(D+1) opening.
/// </summary>
public interface IShiftCycleService
{
    /// <summary>
    /// Returns the ordered shifts for a calendar date: ["B", "C", "A"].
    /// </summary>
    IReadOnlyList<string> GetShiftsForDate(DateTime date);

    /// <summary>
    /// Returns the standard user-facing display order for shifts: ["B", "C", "A"].
    /// </summary>
    IReadOnlyList<string> GetDisplayShifts();

    /// <summary>
    /// Returns the exact predecessor (Date, ShiftType) for meter/stock continuity.
    /// Shift B on D -> (D-1, "A")
    /// Shift C on D -> (D, "B")
    /// Shift A on D -> (D, "C")
    /// </summary>
    (DateTime Date, string ShiftType) GetPreviousShift(DateTime date, string shiftType);

    /// <summary>
    /// Returns the exact successor (Date, ShiftType) for operational handover.
    /// Shift B on D -> (D, "C")
    /// Shift C on D -> (D, "A")
    /// Shift A on D -> (D+1, "B")
    /// </summary>
    (DateTime Date, string ShiftType) GetNextShift(DateTime date, string shiftType);

    /// <summary>
    /// Returns the opening shift of the calendar day: "B".
    /// </summary>
    string GetOpeningShiftForDate(DateTime date);

    /// <summary>
    /// Returns the closing shift of the calendar day: "A".
    /// </summary>
    string GetClosingShiftForDate(DateTime date);

    /// <summary>
    /// Canonical normalizer mapping Roman numerals (I -> A, II -> B, III -> C) and trims.
    /// </summary>
    string NormalizeShiftType(string shiftType);
}
