namespace FuelPro.Core.DTOs;

/// <summary>
/// Data for the Shift Summary print — lists all DSM entries for a shift.
/// </summary>
public class ShiftSummaryPrintData
{
    public string StationName { get; set; } = "Mitali Service Station";
    public string Date        { get; set; } = string.Empty;
    public string Shift       { get; set; } = string.Empty; // "A", "B", "C"
    public string PrintedAt   { get; set; } = string.Empty;

    public List<ShiftSummaryDsmRow> Rows { get; set; } = new();

    public double TotalSales      { get; set; }
    public double TotalCollection { get; set; }
    public double TotalMismatch   { get; set; }
}

public class ShiftSummaryDsmRow
{
    public string DsmName       { get; set; } = string.Empty;
    public int    PumpId        { get; set; }
    public double GrossSales    { get; set; }
    public double TotalCollection { get; set; }
    public double Mismatch      { get; set; }
}
