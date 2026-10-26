using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace FuelPro.Core.DTOs;

public class NozzleDisplayItem
{
    public int NozzleNumber { get; set; }
    public string FuelType { get; set; } = string.Empty;
    public double OpeningReading { get; set; }
    public double ClosingReading { get; set; }
    public double SaleLitres { get; set; }
    public bool HasReading { get; set; }
    public string NozzleName => $"Nozzle {NozzleNumber}";
}

public class NozzleGroupDto : INotifyPropertyChanged
{
    public int PumpId { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string _groupName = string.Empty;
    public string GroupName
    {
        get => _groupName;
        set { _groupName = value; OnPropertyChanged(); }
    }

    private string _fuelType = string.Empty;
    public string FuelType
    {
        get => _fuelType;
        set { _fuelType = value; OnPropertyChanged(); }
    }

    private bool _hasManualDipEntry;
    public bool HasManualDipEntry
    {
        get => _hasManualDipEntry;
        set { _hasManualDipEntry = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMeasurement)); OnPropertyChanged(nameof(StockVarianceDisplay)); OnPropertyChanged(nameof(StockVarianceStatus)); }
    }

    private bool? _hasPhysicalDip;
    public bool? HasPhysicalDip
    {
        get => _hasPhysicalDip;
        set { _hasPhysicalDip = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMeasurement)); OnPropertyChanged(nameof(StockVarianceDisplay)); OnPropertyChanged(nameof(StockVarianceStatus)); }
    }

    private string? _dipSource;
    public string? DipSource
    {
        get => _dipSource;
        set { _dipSource = value; OnPropertyChanged(); }
    }

    public bool HasMeasurement
    {
        get
        {
            if (HasPhysicalDip.HasValue)
            {
                return HasPhysicalDip.Value || HasManualDipEntry;
            }
            return HasManualDipEntry || (Dip > 0.001) || (_hasExplicitStock && Stock > 0.001);
        }
    }

    private double _dip;
    public double Dip
    {
        get => _dip;
        set 
        { 
            _dip = Math.Round(value, 2); 
            if (value > 0.001)
            {
                HasPhysicalDip = true;
                HasManualDipEntry = true;
                DipSource ??= "Manual";
            }
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(HasMeasurement));
            OnPropertyChanged(nameof(DipMm));
            OnPropertyChanged(nameof(DipDisplay));
            OnPropertyChanged(nameof(StockVariance));
            OnPropertyChanged(nameof(StockVarianceDisplay));
            OnPropertyChanged(nameof(StockVarianceStatus));
        }
    }

    public void RecordManualDip(double dip, double measuredStock)
    {
        _dip = Math.Round(dip, 2);
        _stock = Math.Round(measuredStock, 2);
        HasManualDipEntry = true;
        HasPhysicalDip = true;
        _hasExplicitStock = true;
        DipSource = "Manual";
        OnPropertyChanged(nameof(Dip));
        OnPropertyChanged(nameof(Stock));
        OnPropertyChanged(nameof(HasMeasurement));
        OnPropertyChanged(nameof(DipMm));
        OnPropertyChanged(nameof(DipDisplay));
        OnPropertyChanged(nameof(StockVariance));
        OnPropertyChanged(nameof(StockVarianceDisplay));
        OnPropertyChanged(nameof(StockVarianceStatus));
    }

    private bool _hasExplicitStock;
    private double _stock;
    public double Stock
    {
        get => _stock;
        set 
        { 
            _stock = Math.Round(value, 2); 
            if (value > 0.001)
            {
                _hasExplicitStock = true;
            }
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(StockLtr));
            OnPropertyChanged(nameof(StockVariance));
            OnPropertyChanged(nameof(StockVarianceDisplay));
            OnPropertyChanged(nameof(StockVarianceStatus));
            OnPropertyChanged(nameof(HasMeasurement));
        }
    }

    private double _density;
    public double Density
    {
        get => _density;
        set { _density = Math.Round(value, 2); OnPropertyChanged(); }
    }

    private double _openingStock;
    public double OpeningStock
    {
        get => _openingStock;
        set { _openingStock = Math.Round(value, 2); OnPropertyChanged(); RecalcCalculatedStock(); }
    }

    private double _fuelDispensed;
    public double FuelDispensed
    {
        get => _fuelDispensed;
        set { _fuelDispensed = Math.Round(value, 2); OnPropertyChanged(); RecalcCalculatedStock(); }
    }

    private double _testingLitres;
    public double TestingLitres
    {
        get => _testingLitres;
        set { _testingLitres = Math.Round(value, 2); OnPropertyChanged(); RecalcCalculatedStock(); }
    }

    private double _receipts;
    public double Receipts
    {
        get => _receipts;
        set { _receipts = Math.Round(value, 2); OnPropertyChanged(); RecalcCalculatedStock(); }
    }

    private double _calculatedStock;
    public double CalculatedStock
    {
        get => _calculatedStock;
        private set 
        { 
            _calculatedStock = Math.Round(value, 2); 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(StockVariance));
            OnPropertyChanged(nameof(AutomaticStockDisplay));
            OnPropertyChanged(nameof(StockVarianceDisplay));
            OnPropertyChanged(nameof(StockVarianceStatus));
        }
    }

    private void RecalcCalculatedStock()
    {
        double oldCalculated = _calculatedStock;
        CalculatedStock = Math.Round(OpeningStock - FuelDispensed + TestingLitres + Receipts, 2);
        if (!HasMeasurement || Stock == 0 || Math.Abs(Stock - oldCalculated) < 0.001)
        {
            if (!HasMeasurement)
            {
                Stock = CalculatedStock;
            }
        }
        OnPropertyChanged(nameof(StockVariance));
        OnPropertyChanged(nameof(AutomaticStockDisplay));
        OnPropertyChanged(nameof(StockVarianceDisplay));
        OnPropertyChanged(nameof(StockVarianceStatus));
    }

    public List<List<NozzleDisplayItem>> Rows { get; set; } = new();

    public IEnumerable<NozzleDisplayItem> FlatNozzles => Rows?.SelectMany(r => r) ?? Enumerable.Empty<NozzleDisplayItem>();
    public IEnumerable<NozzleDisplayItem> Nozzles => FlatNozzles;

    public double DipMm => Dip;
    public double StockLtr => Stock;
    public double SaleLitres => FuelDispensed;
    public double StockVariance => Stock - CalculatedStock;

    public bool HasValidOpeningStock => OpeningStock > 0;
    public string AutomaticStockDisplay => HasValidOpeningStock ? CalculatedStock.ToString("N2") : "—";
    
    public string DipDisplay => IsCng ? "N/A" : (HasMeasurement ? Dip.ToString("N2") : "Dip Not Recorded");

    public string StockVarianceDisplay
    {
        get
        {
            if (IsCng) return "N/A";
            if (!HasValidOpeningStock || !HasMeasurement) return "—";
            return StockVariance >= 0 ? $"+{StockVariance:N2}" : $"{StockVariance:N2}";
        }
    }

    public string StockVarianceStatus
    {
        get
        {
            if (IsCng) return "N/A";
            if (!HasMeasurement) return "Dip Not Recorded";
            if (Math.Abs(StockVariance) < 0.001) return "Balanced";
            return StockVariance > 0 ? "Excess" : "Shortage";
        }
    }

    public bool IsCng => string.Equals(FuelType, "CNG", System.StringComparison.OrdinalIgnoreCase) || 
                         (GroupName != null && GroupName.Contains("CNG", System.StringComparison.OrdinalIgnoreCase));
    public string Unit => IsCng ? "Kg" : "L";
    public string FullUnit => IsCng ? "Kg" : "Ltr";
}

public class FuelTypeReadingSummary
{
    public string FuelType { get; set; } = string.Empty;
    public string FuelTypeLabel => FuelPro.Core.Common.FuelTypeExtensions.ToFriendlyLabel(FuelType);
    public int NozzleCount { get; set; }
    public double TotalLitres { get; set; }
    public double Rate { get; set; }
    public double TotalAmount { get; set; }
    public bool IsCng => string.Equals(FuelType, "CNG", System.StringComparison.OrdinalIgnoreCase);
    public string Unit => IsCng ? "Kg" : "L";
    public string DisplayText => $"{TotalLitres:N2} {Unit} × ₹{Rate:N2} = ₹{TotalAmount:N2}";
    
    public string BadgeBackground
    {
        get
        {
            var f = (FuelType ?? "").Trim().ToUpperInvariant();
            if (f.Contains("MS") || f.Contains("PETROL")) return "#E3F2FD"; // Light Blue
            if (f.Contains("HSD") || f.Contains("DIESEL")) return "#FFF3E0"; // Light Orange
            if (f.Contains("SPEED") || f.Contains("POWER") || f.Contains("XP95")) return "#F3E5F5"; // Light Purple
            if (f.Contains("CNG")) return "#E8F5E9"; // Light Green
            return "#ECEFF1"; // Light Grey
        }
    }

    public string BadgeForeground
    {
        get
        {
            var f = (FuelType ?? "").Trim().ToUpperInvariant();
            if (f.Contains("MS") || f.Contains("PETROL")) return "#0D47A1"; // Deep Blue
            if (f.Contains("HSD") || f.Contains("DIESEL")) return "#E65100"; // Deep Orange
            if (f.Contains("SPEED") || f.Contains("POWER") || f.Contains("XP95")) return "#4A148C"; // Deep Purple
            if (f.Contains("CNG")) return "#1B5E20"; // Deep Green
            return "#37474F"; // Deep Slate
        }
    }

    public string BadgeBorder
    {
        get
        {
            var f = (FuelType ?? "").Trim().ToUpperInvariant();
            if (f.Contains("MS") || f.Contains("PETROL")) return "#90CAF9";
            if (f.Contains("HSD") || f.Contains("DIESEL")) return "#FFCC80";
            if (f.Contains("SPEED") || f.Contains("POWER") || f.Contains("XP95")) return "#CE93D8";
            if (f.Contains("CNG")) return "#A5D6A7";
            return "#CFD8DC";
        }
    }
}
