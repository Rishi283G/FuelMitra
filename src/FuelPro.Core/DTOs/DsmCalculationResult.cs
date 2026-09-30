namespace FuelPro.Core.DTOs;

public class DsmCalculationResult
{
    public decimal GrossSales { get; set; }
    public decimal MeterGrossSales { get => GrossSales; set => GrossSales = value; }
    public decimal TotalTesting { get; set; }
    public decimal NetGrossSales { get; set; }
    public decimal TotalInDirect { get; set; }
    public decimal TotalCreditors { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal TotalCollection { get; set; }
    public decimal Mismatch { get; set; }
    public bool IsBalanced { get; set; }

    public static DsmCalculationResult Empty()
    {
        return new DsmCalculationResult
        {
            GrossSales = 0m,
            TotalTesting = 0m,
            NetGrossSales = 0m,
            TotalInDirect = 0m,
            TotalCreditors = 0m,
            TotalExpenses = 0m,
            TotalCollection = 0m,
            Mismatch = 0m,
            IsBalanced = false
        };
    }
}
