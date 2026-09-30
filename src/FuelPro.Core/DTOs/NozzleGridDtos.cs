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

    private double _dip;
    public double Dip
    {
        get => _dip;
        set { _dip = Math.Round(value, 2); OnPropertyChanged(); }
    }

    private double _stock;
    public double Stock
    {
        get => _stock;
        set { _stock = Math.Round(value, 2); OnPropertyChanged(); }
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
        private set { _calculatedStock = Math.Round(value, 2); OnPropertyChanged(); }
    }

    private void RecalcCalculatedStock()
    {
        double oldCalculated = _calculatedStock;
        CalculatedStock = Math.Round(OpeningStock - FuelDispensed + TestingLitres + Receipts, 2);
        if (Stock == 0 || Math.Abs(Stock - oldCalculated) < 0.001)
        {
            Stock = CalculatedStock;
        }
    }

    public List<List<NozzleDisplayItem>> Rows { get; set; } = new();

    public IEnumerable<NozzleDisplayItem> FlatNozzles => Rows?.SelectMany(r => r) ?? Enumerable.Empty<NozzleDisplayItem>();
    public IEnumerable<NozzleDisplayItem> Nozzles => FlatNozzles;

    public double DipMm => Dip;
    public double StockLtr => Stock;
    public double SaleLitres => FuelDispensed;
    public double StockVariance => Stock - CalculatedStock;

    public bool IsCng => string.Equals(FuelType, "CNG", System.StringComparison.OrdinalIgnoreCase) || 
                         (GroupName != null && GroupName.Contains("CNG", System.StringComparison.OrdinalIgnoreCase));
    public string Unit => IsCng ? "Kg" : "L";
    public string FullUnit => IsCng ? "Kg" : "Ltr";
}
