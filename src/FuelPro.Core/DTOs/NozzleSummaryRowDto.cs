namespace FuelPro.Core.DTOs;

public class NozzleSummaryRowDto
{
    public int PumpId { get; set; }
    public string FuelType { get; set; } = "";
    public double OpeningReading { get; set; }
    public double ClosingReading { get; set; }
    public double GrossLitres { get; set; }
    public double NetSaleLitres { get; set; }
    public double Rate { get; set; }
    public double Amount { get; set; }
}
