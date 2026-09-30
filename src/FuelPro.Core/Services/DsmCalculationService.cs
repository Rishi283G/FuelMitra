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
            var totalTesting = dsm.TestingEntries.Sum(x => x.Amount);
            var netGrossSales = grossSales - totalTesting;

            var totalInDirect = dsm.PaymentCollection.PhonePe + dsm.PaymentCollection.CreditCard
                + dsm.PaymentCollection.CashDeposit + dsm.PaymentCollection.PhysicalCash
                + dsm.PaymentCollection.PetroCard + dsm.PaymentCollection.DynamicPayments;
            var totalCreditors = dsm.DebitEntries.Sum(x => x.Amount);
            var totalExpenses = dsm.Expenses.Sum(x => x.Amount);

            // Canonical Option B: Testing is excluded from TotalCollection
            var totalCollection = totalInDirect + totalCreditors + totalExpenses;
            var mismatch = totalCollection - netGrossSales;

            return new DsmCalculationResult
            {
                GrossSales = grossSales,
                TotalTesting = totalTesting,
                NetGrossSales = netGrossSales,
                TotalInDirect = totalInDirect,
                TotalCreditors = totalCreditors,
                TotalExpenses = totalExpenses,
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
