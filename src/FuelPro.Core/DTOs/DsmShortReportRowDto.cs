namespace FuelPro.Core.DTOs;

public class DsmShortReportRowDto
{
    public DateTime Date { get; set; }
    public string ShiftType { get; set; } = string.Empty;
    public double ShortAmount { get; set; }
}
