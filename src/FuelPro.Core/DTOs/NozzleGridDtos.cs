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

    private bool _hasManualStockEntry;
    public bool HasManualStockEntry
    {
        get => _hasManualStockEntry;
        set { _hasManualStockEntry = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMeasurement)); OnPropertyChanged(nameof(StockVarianceDisplay)); OnPropertyChanged(nameof(StockVarianceStatus)); }
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
            if (HasPhysicalDip.HasValue && !HasPhysicalDip.Value) return false;
            if (HasManualStockEntry && ManualStock.HasValue) return true;
            if (HasManualDipEntry && (Dip.HasValue && (Dip.Value > 0.001 || HasPhysicalDip == true))) return true;
            if (HasPhysicalDip.HasValue && HasPhysicalDip.Value && (ManualStock.HasValue || Dip.HasValue)) return true;
            return false;
        }
    }

    private double? _dip;
    public double? Dip
    {
        get => _dip;
        set 
        { 
            _dip = value.HasValue ? Math.Round(value.Value, 2) : (double?)null; 
            if (value.HasValue)
            {
                if (value.Value > 0.001 || HasPhysicalDip == true || HasManualDipEntry)
                {
                    HasPhysicalDip = true;
                    HasManualDipEntry = true;
                    DipSource ??= "Manual";
                }
            }
            else
            {
                HasManualDipEntry = false;
                if (!HasManualStockEntry)
                {
                    HasPhysicalDip = false;
                    DipSource = null;
                }
            }
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(DipMm));
            OnPropertyChanged(nameof(DipDisplay));
            OnPropertyChanged(nameof(HasMeasurement));
            OnPropertyChanged(nameof(PhysicalStock));
            OnPropertyChanged(nameof(StockVariance));
            OnPropertyChanged(nameof(StockVarianceDisplay));
            OnPropertyChanged(nameof(StockVarianceStatus));
        }
    }

    private double? _manualStock;
    public double? ManualStock
    {
        get => _manualStock;
        set
        {
            _manualStock = value.HasValue ? Math.Round(value.Value, 2) : (double?)null;
            if (value.HasValue && value.Value >= 0)
            {
                _hasExplicitStock = true;
                HasManualStockEntry = true;
                HasPhysicalDip = true;
                DipSource ??= "Manual";
            }
            else
            {
                _hasExplicitStock = false;
                HasManualStockEntry = false;
                if (!HasManualDipEntry)
                {
                    HasPhysicalDip = false;
                    DipSource = null;
                }
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(Stock));
            OnPropertyChanged(nameof(StockLtr));
            OnPropertyChanged(nameof(PhysicalStock));
            OnPropertyChanged(nameof(HasMeasurement));
            OnPropertyChanged(nameof(StockVariance));
            OnPropertyChanged(nameof(StockVarianceDisplay));
            OnPropertyChanged(nameof(StockVarianceStatus));
        }
    }

    public double? PhysicalStock => (HasManualStockEntry && ManualStock.HasValue) ? ManualStock : null;

    public void RecordManualDip(double dip, double measuredStock)
    {
        Dip = dip;
        ManualStock = measuredStock;
        DipSource = "Manual";
    }

    private bool _hasExplicitStock;

    public double Stock
    {
        get => ManualStock ?? CalculatedStock;
        set
        {
            if (HasPhysicalDip == false && !HasManualStockEntry)
            {
                // Unmeasured compatibility assignment: do not create manual measurement
            }
            else
            {
                ManualStock = value;
            }
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

    public void SetCalculatedStockInternal(double value)
    {
        CalculatedStock = value;
    }

    public void RecalcCalculatedStock()
    {
        CalculatedStock = Math.Round(OpeningStock - FuelDispensed + TestingLitres + Receipts, 2);
        OnPropertyChanged(nameof(Stock));
        OnPropertyChanged(nameof(StockLtr));
        OnPropertyChanged(nameof(StockVariance));
        OnPropertyChanged(nameof(AutomaticStockDisplay));
        OnPropertyChanged(nameof(StockVarianceDisplay));
        OnPropertyChanged(nameof(StockVarianceStatus));
    }

    public List<List<NozzleDisplayItem>> Rows { get; set; } = new();

    public IEnumerable<NozzleDisplayItem> FlatNozzles => Rows?.SelectMany(r => r) ?? Enumerable.Empty<NozzleDisplayItem>();
    public IEnumerable<NozzleDisplayItem> Nozzles => FlatNozzles;

    public double DipMm => Dip.GetValueOrDefault(0.0);
    public double StockLtr => PhysicalStock ?? CalculatedStock;
    public double SaleLitres => FuelDispensed;
    public double StockVariance => PhysicalStock.HasValue ? Math.Round(PhysicalStock.Value - CalculatedStock, 2) : 0.0;

    public bool HasValidOpeningStock => OpeningStock > 0;
    public string AutomaticStockDisplay => HasValidOpeningStock ? CalculatedStock.ToString("N2") : "—";
    
    public string DipDisplay => IsCng ? "N/A" : ((Dip.HasValue && Dip.Value > 0.001) ? Dip.Value.ToString("N2") : (HasMeasurement ? "0.00" : "Dip Not Recorded"));

    public string StockVarianceDisplay
    {
        get
        {
            if (!HasMeasurement || !PhysicalStock.HasValue || !HasValidOpeningStock) return "—";
            var diff = Math.Round(PhysicalStock.Value - CalculatedStock, 2);
            return diff >= 0 ? $"+{diff:N2}" : $"{diff:N2}";
        }
    }

    public string StockVarianceStatus
    {
        get
        {
            if (!HasMeasurement) return IsCng ? "N/A" : "Dip Not Recorded";
            if (!PhysicalStock.HasValue) return "Dip Recorded";
            var diff = Math.Round(PhysicalStock.Value - CalculatedStock, 2);
            if (Math.Abs(diff) < 0.001) return "Balanced";
            return diff > 0 ? "Excess" : "Shortage";
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
    public double SaleLitres => TotalLitres;
    public double Rate { get; set; }
    public double TotalAmount { get; set; }
    public double Amount => TotalAmount;
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
