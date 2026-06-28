using System.Collections.Generic;

namespace FuelPro.Core.DTOs;

public class GenericGridPrintCard
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Highlight { get; set; }
}

public class GenericGridPrintData
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public List<GenericGridPrintCard> SummaryCards { get; set; } = new();
    public List<string> Headers { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();
    public bool ShowSignatures { get; set; }
}
