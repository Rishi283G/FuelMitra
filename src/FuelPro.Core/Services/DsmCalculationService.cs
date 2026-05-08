using FuelPro.Core.DTOs;
using Serilog;

namespace FuelPro.Core.Services;

public class DsmCalculationService : IDsmCalculationService
{
    private readonly ILogger _logger = Log.ForContext<DsmCalculationService>();

    public DsmCalculationResult Calculate(DsmEntryDto dsm)
    {
        try
        {
            var grossSales = dsm.NozzleReadings.Sum(x => x.Amount);
            var totalInDirect = dsm.PaymentCollection.PhonePe + dsm.PaymentCollection.CreditCard
                + dsm.PaymentCollection.CashDeposit + dsm.PaymentCollection.PhysicalCash;
            var totalCreditors = dsm.DebitEntries.Sum(x => x.Amount);
            var totalTesting = dsm.TestingEntries.Sum(x => x.Amount);
            var totalExpenses = dsm.Expenses.Sum(x => x.Amount);
            var totalCollection = totalInDirect + totalCreditors + totalTesting + totalExpenses;
            var mismatch = totalCollection - grossSales;

            return new DsmCalculationResult
            {
                GrossSales = grossSales,
                TotalInDirect = totalInDirect,
                TotalCreditors = totalCreditors,
                TotalCollection = totalCollection,
                Mismatch = mismatch,
                IsBalanced = mismatch == 0m
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Calculation failed for DSMEntryId: {Id}", dsm.DSMEntryId);
            return DsmCalculationResult.Empty();
        }
    }

    public static void EnsureNozzleReadingValid(decimal openingReading, decimal closingReading)
    {
        if (closingReading < openingReading)
        {
            throw new ArgumentException("Closing cannot be less than opening");
        }
    }
}
