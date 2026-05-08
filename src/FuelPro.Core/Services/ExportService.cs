using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using CsvHelper;
using System.Globalization;
using System.Text;
using Serilog;

namespace FuelPro.Core.Services;

public class ExportService
{
    private readonly ILogger _logger = Log.ForContext<ExportService>();

    public Result ExportFinalCalculationToCsv(FinalCalculationDto calc, string filePath)
    {
        try
        {
            using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
            writer.WriteLine($"Shift Report - {calc.ShiftDate:dd/MM/yyyy} - Shift {calc.ShiftType}");
            writer.WriteLine();

            // TABLE A
            writer.WriteLine("DSM Summary");
            writer.WriteLine("DSM Name,Total In (Direct),Creditors,Total Collection,Mismatch");
            foreach (var r in calc.DsmSummaryRows)
            {
                var totalInDirect = r.PhonePeCard + r.PhonePe + r.CreditCard + r.PetroCard + r.CashDeposit + r.CashInHand;
                var totalCollection = totalInDirect + r.Debit;
                var mismatch = calc.TotalFuelSaleAmount - totalCollection;
                writer.WriteLine($"{r.DsmName},{totalInDirect},{r.Debit},{totalCollection},{mismatch}");
            }
            writer.WriteLine();

            // TABLE C
            writer.WriteLine("Debit Register");
            writer.WriteLine("DSM Name,Debtor Name,Amount");
            foreach (var r in calc.DebitRegisterRows)
                writer.WriteLine($"{r.DsmName},{r.DebtorName},{r.Amount}");
            writer.WriteLine($"Total,,{calc.TotalDebit}");
            writer.WriteLine();

            // TABLE D
            writer.WriteLine("Expenses Register");
            writer.WriteLine("DSM Name,Description,Amount");
            foreach (var r in calc.ExpenseRegisterRows)
                writer.WriteLine($"{r.DsmName},{r.Description},{r.Amount}");
            writer.WriteLine($"Total,,{calc.TotalExpenses}");
            writer.WriteLine();

            // TABLE E
            writer.WriteLine("Fuel Dispensed");
            writer.WriteLine("Fuel Type,Litres,Rate,Amount");
            writer.WriteLine($"HSD,{calc.HsdLitres},{calc.HsdRate},{calc.HsdAmount}");
            writer.WriteLine($"MS-I,{calc.MsILitres},{calc.MsIRate},{calc.MsIAmount}");
            writer.WriteLine($"MS-II,{calc.MsIILitres},{calc.MsIIRate},{calc.MsIIAmount}");
            writer.WriteLine($"Total,{calc.TotalLitres},,{calc.TotalFuelSaleAmount}");
            writer.WriteLine();

            // TABLE F
            writer.WriteLine("Final Reconciliation");
            writer.WriteLine($"PhonePe Total,{calc.PhonePeTotal}");
            writer.WriteLine($"Petro Card,{calc.PetroCardTotal}");
            writer.WriteLine($"Debit,{calc.TotalDebit}");
            writer.WriteLine($"PineLab Card,{calc.CreditCardTotal}");
            writer.WriteLine($"Bank Cash,{calc.BankCash}");
            writer.WriteLine($"Cash in Hand,{calc.CashInHand}");
            writer.WriteLine($"TOTAL,{calc.TotalAmounts}");
            writer.WriteLine($"Fuel Sale Total,{calc.TotalFuelSaleAmount}");
            writer.WriteLine($"Mismatch,{calc.ReconciliationDifference}");

            _logger.Information("CSV exported to {FilePath}", filePath);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "CSV export failed");
            return Result.Fail($"Export failed: {ex.Message}");
        }
    }
}
