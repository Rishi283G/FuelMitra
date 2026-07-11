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
}

public class NozzleGroupDto : INotifyPropertyChanged
{
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
        set { _dip = value; OnPropertyChanged(); }
    }

    private double _stock;
    public double Stock
    {
        get => _stock;
        set { _stock = value; OnPropertyChanged(); }
    }

    private double _density;
    public double Density
    {
        get => _density;
        set { _density = value; OnPropertyChanged(); }
    }

    private double _openingStock;
    public double OpeningStock
    {
        get => _openingStock;
        set { _openingStock = value; OnPropertyChanged(); RecalcCalculatedStock(); }
    }

    private double _fuelDispensed;
    public double FuelDispensed
    {
        get => _fuelDispensed;
        set { _fuelDispensed = value; OnPropertyChanged(); RecalcCalculatedStock(); }
    }

    private double _testingLitres;
    public double TestingLitres
    {
        get => _testingLitres;
        set { _testingLitres = value; OnPropertyChanged(); RecalcCalculatedStock(); }
    }

    private double _receipts;
    public double Receipts
    {
        get => _receipts;
        set { _receipts = value; OnPropertyChanged(); RecalcCalculatedStock(); }
    }

    private double _calculatedStock;
    public double CalculatedStock
    {
        get => _calculatedStock;
        private set { _calculatedStock = value; OnPropertyChanged(); }
    }

    private void RecalcCalculatedStock()
    {
        double oldCalculated = _calculatedStock;
        CalculatedStock = OpeningStock - FuelDispensed + TestingLitres + Receipts;
        if (Stock == 0 || Math.Abs(Stock - oldCalculated) < 0.001)
        {
            Stock = CalculatedStock;
        }
    }

    public List<List<NozzleDisplayItem>> Rows { get; set; } = new();

    public IEnumerable<NozzleDisplayItem> FlatNozzles => Rows?.SelectMany(r => r) ?? Enumerable.Empty<NozzleDisplayItem>();
}
