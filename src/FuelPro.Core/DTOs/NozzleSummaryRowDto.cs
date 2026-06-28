using FuelPro.Core.Common;

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
    public string FuelTypeLabel
    {
        get
        {
            var clean = FuelType?.Replace("-", "_") ?? "";
            if (System.Enum.TryParse<FuelPro.Core.Common.FuelType>(clean, out var parsed))
            {
                return parsed.ToFriendlyLabel();
            }
            return FuelType;
        }
    }

    public string GrossLitresDisplay => FuelType == "CNG" ? $"{GrossLitres:F4} Kg" : $"{GrossLitres:F4} L";
    public string NetSaleLitresDisplay => FuelType == "CNG" ? $"{NetSaleLitres:F4} Kg" : $"{NetSaleLitres:F4} L";
}
