using System;
using System.Collections.Generic;

namespace FuelPro.Core.Services;

/// <summary>
/// Definitive service managing operational shift sequences and continuity tuples.
/// Station operational order per calendar day: Shift A -> Shift B -> Shift C.
/// Continuous handover chain: C(D-1) closing -> A(D) opening -> A(D) closing -> B(D) opening -> B(D) closing -> C(D) opening -> C(D) closing -> A(D+1) opening.
/// </summary>
public interface IShiftCycleService
{
    /// <summary>
    /// Returns the ordered shifts for a calendar date: ["A", "B", "C"].
    /// </summary>
    IReadOnlyList<string> GetShiftsForDate(DateTime date);

    /// <summary>
    /// Returns the standard user-facing display order for shifts: ["A", "B", "C"].
    /// </summary>
    IReadOnlyList<string> GetDisplayShifts();

    /// <summary>
    /// Returns the exact predecessor (Date, ShiftType) for meter/stock continuity.
    /// Shift A on D -> (D-1, "C")
    /// Shift B on D -> (D, "A")
    /// Shift C on D -> (D, "B")
    /// </summary>
    (DateTime Date, string ShiftType) GetPreviousShift(DateTime date, string shiftType);

    /// <summary>
    /// Returns the exact successor (Date, ShiftType) for operational handover.
    /// Shift A on D -> (D, "B")
    /// Shift B on D -> (D, "C")
    /// Shift C on D -> (D+1, "A")
    /// </summary>
    (DateTime Date, string ShiftType) GetNextShift(DateTime date, string shiftType);

    /// <summary>
    /// Returns the opening shift of the calendar day: "A".
    /// </summary>
    string GetOpeningShiftForDate(DateTime date);

    /// <summary>
    /// Returns the closing shift of the calendar day: "C".
    /// </summary>
    string GetClosingShiftForDate(DateTime date);

    /// <summary>
    /// Canonical normalizer mapping Roman numerals (I -> A, II -> B, III -> C) and trims.
    /// </summary>
    string NormalizeShiftType(string shiftType);
}
