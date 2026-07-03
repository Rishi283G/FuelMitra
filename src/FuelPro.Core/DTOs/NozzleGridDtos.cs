using System.Collections.Generic;
using System.Linq;

namespace FuelPro.Core.DTOs;

public class NozzleDisplayItem
{
    public int NozzleNumber { get; set; }
    public string FuelType { get; set; } = string.Empty;
    public double OpeningReading { get; set; }
    public double ClosingReading { get; set; }
    public double SaleLitres { get; set; }
    public bool HasReading { get; set; }
}

public class NozzleGroupDto
{
    public string GroupName { get; set; } = string.Empty; // e.g., "HSD - 20 KL"
    public string FuelType { get; set; } = string.Empty;  // "HSD", "MS-I", "MS-II"
    public double Dip { get; set; }
    public double Stock { get; set; }
    public double Density { get; set; }
    public List<List<NozzleDisplayItem>> Rows { get; set; } = new();

    public IEnumerable<NozzleDisplayItem> FlatNozzles => Rows?.SelectMany(r => r) ?? Enumerable.Empty<NozzleDisplayItem>();
}
