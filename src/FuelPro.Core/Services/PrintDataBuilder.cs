using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using Serilog;

namespace FuelPro.Core.Services;

/// <summary>
/// Builds a <see cref="FinalCalcPrintData"/> from the data already aggregated
/// by FinalCalculationViewModel — does NOT change any calculation logic.
/// </summary>
public class PrintDataBuilder
{
    private readonly ILogger _logger = Log.ForContext<PrintDataBuilder>();

    /// <summary>
    /// Constructs the print data object from all shift-level aggregated values.
    /// All numeric values come directly from the ViewModel without recalculation.
    /// </summary>
    public FinalCalcPrintData BuildPrintData(
        DateTime date,
        string shiftType,           // "A", "B", "C"
        string stationName,
        List<DsmSummaryRowDto> dsmRows,
        CashAggregateDto cash1Agg,
        CashAggregateDto cash2Agg,
        List<DebitRegisterRowDto> creditorRows,
        double hsdLitres, double hsdRate, double hsdAmount,
        double msILitres, double msIRate, double msIAmount,
        double msIILitres, double msIIRate, double msIIAmount,
        double cngLitres, double cngRate, double cngAmount,
        double otherCashTotal,
        double reconciliationMsTesting,
        double reconciliationHsdTesting,
        double reconciliationHsdTesting2,
        double reconciliationCngTesting,
        double phonePeTotal,
        double phonePeMorningTotal,
        double phonePeNightTotal,
        double phonePeCardTotal,
        double phonePeCardMorningTotal,
        double phonePeCardNightTotal,
        double creditCardMorningTotal,
        double creditCardNightTotal,
        double petroCardTotal,
        double bankCash,
        double cashInHand,
        double expensesTotal,
        double reconciliationTotal,
        double grossFuelSaleTotal,
        double totalDsmShort,
        List<ExpenseRegisterRowDto>? expenseRows = null,
        List<CreditorRepayment>? sameDayRepayments = null)
    {
        try
        {
            var shiftLabel = shiftType switch
            {
                "A" => "I",
                "B" => "II",
                "C" => "III",
                _ => shiftType
            };

            // DSM rows
            var dsmPrintRows = dsmRows.Select(r => new DsmPrintRow
            {
                DsmName    = r.DsmName,
                PumpNo     = r.PumpId,
                CardAmount = (decimal)(r.CreditCardMorning + r.CreditCardNight),
                CreditCardMorning = (decimal)r.CreditCardMorning,
                CreditCardNight   = (decimal)r.CreditCardNight,
                PhonePay   = (decimal)(r.PhonePeMorning + r.PhonePeNight + r.PhonePeCardMorning + r.PhonePeCardNight),
                PhonePeMorning = (decimal)r.PhonePeMorning,
                PhonePeNight   = (decimal)r.PhonePeNight,
                PhonePeCardMorning = (decimal)r.PhonePeCardMorning,
                PhonePeCardNight = (decimal)r.PhonePeCardNight,
                PetroCard  = (decimal)r.PetroCard,
                Debit      = (decimal)r.Debit,
                Expenses   = (decimal)r.Expenses,
                Testing    = (decimal)r.Testing,
                Cash1      = (decimal)r.CashDeposit,
                Cash2      = (decimal)r.CashInHand,
                GrossSale  = (decimal)r.GrossSales
            }).ToList();

            // Cash 1 — Bank Deposit (only 500/200/100 shown per spec)
            var c1 = new CashPrintBlock
            {
                Count500 = cash1Agg.Total500,
                Amt500   = (decimal)(cash1Agg.Total500 * 500.0),
                Count200 = cash1Agg.Total200,
                Amt200   = (decimal)(cash1Agg.Total200 * 200.0),
                Count100 = cash1Agg.Total100,
                Amt100   = (decimal)(cash1Agg.Total100 * 100.0),
                Count50  = cash1Agg.Total50,
                Amt50    = (decimal)(cash1Agg.Total50 * 50.0),
                Count20  = cash1Agg.Total20,
                Amt20    = (decimal)(cash1Agg.Total20 * 20.0),
                Count10  = cash1Agg.Total10,
                Amt10    = (decimal)(cash1Agg.Total10 * 10.0),
                CoinAmt  = (decimal)cash1Agg.TotalCoins,
                Total    = (decimal)cash1Agg.GrandTotal
            };

            // Cash 2 — Cash In Hand (all denominations)
            var c2 = new CashPrintBlock
            {
                Count500 = cash2Agg.Total500,
                Amt500   = (decimal)(cash2Agg.Total500 * 500.0),
                Count200 = cash2Agg.Total200,
                Amt200   = (decimal)(cash2Agg.Total200 * 200.0),
                Count100 = cash2Agg.Total100,
                Amt100   = (decimal)(cash2Agg.Total100 * 100.0),
                Count50  = cash2Agg.Total50,
                Amt50    = (decimal)(cash2Agg.Total50 * 50.0),
                Count20  = cash2Agg.Total20,
                Amt20    = (decimal)(cash2Agg.Total20 * 20.0),
                Count10  = cash2Agg.Total10,
                Amt10    = (decimal)(cash2Agg.Total10 * 10.0),
                CoinAmt  = (decimal)cash2Agg.TotalCoins,
                Total    = (decimal)cash2Agg.GrandTotal
            };

            var creditorPrintRows = creditorRows.Select(c => new CreditorPrintRow
            {
                Name     = c.DebtorName,
                SlipNo   = string.Empty,
                ChequeNo = c.ChequeNo ?? string.Empty,
                Amount   = (decimal)c.Amount,
                Note     = string.Empty
            }).ToList();

            // Fuel sale rows
            var fuelRows = new List<FuelSalePrintRow>();
            if (hsdLitres > 0 || hsdAmount > 0)
                fuelRows.Add(new FuelSalePrintRow
                {
                    Description = "HSD - 20KL",
                    FuelType    = "HSD",
                    TankLabel   = "20 KL",
                    Litres  = (decimal)hsdLitres,
                    Rate    = (decimal)Math.Round(hsdRate, 2),
                    Amount  = (decimal)hsdAmount
                });
            if (msIILitres > 0 || msIIAmount > 0)
                fuelRows.Add(new FuelSalePrintRow
                {
                    Description = "HSD - 20KL II",
                    FuelType    = "MS-II",
                    TankLabel   = "20 KL II",
                    Litres  = (decimal)msIILitres,
                    Rate    = (decimal)Math.Round(msIIRate, 2),
                    Amount  = (decimal)msIIAmount
                });
            if (msILitres > 0 || msIAmount > 0)
                fuelRows.Add(new FuelSalePrintRow
                {
                    Description = "MS - 20KL",
                    FuelType    = "MS-I",
                    TankLabel   = "20 KL",
                    Litres  = (decimal)msILitres,
                    Rate    = (decimal)Math.Round(msIRate, 2),
                    Amount  = (decimal)msIAmount
                });
            if (cngLitres > 0 || cngAmount > 0)
                fuelRows.Add(new FuelSalePrintRow
                {
                    Description = "CNG - Line",
                    FuelType    = "CNG",
                    TankLabel   = "Line",
                    Litres  = (decimal)cngLitres,
                    Rate    = (decimal)Math.Round(cngRate, 2),
                    Amount  = (decimal)cngAmount
                });

            // Expense rows (informational section for print)
            var expensePrintRows = (expenseRows ?? new List<ExpenseRegisterRowDto>())
                .Select(e => new ExpensePrintRow
                {
                    DsmName     = e.DsmName,
                    Description = e.Description,
                    Amount      = (decimal)e.Amount
                }).ToList();

            // Reconciliation block
            var rec = new ReconciliationPrintBlock
            {
                MsTesting  = (decimal)reconciliationMsTesting,
                HsdTesting = (decimal)reconciliationHsdTesting,
                HsdTesting2 = (decimal)reconciliationHsdTesting2,
                CngTesting = (decimal)reconciliationCngTesting,
                PhonePe    = (decimal)phonePeTotal,
                PhonePeCardMorning = (decimal)phonePeCardMorningTotal,
                PhonePeCardNight   = (decimal)phonePeCardNightTotal,
                PhonePeMorning = (decimal)phonePeMorningTotal,
                PhonePeNight   = (decimal)phonePeNightTotal,
                PetroCard  = (decimal)petroCardTotal,
                Debit      = (decimal)creditorRows.Sum(c => c.Amount),
                CreditCardMorning = (decimal)creditCardMorningTotal,
                CreditCardNight = (decimal)creditCardNightTotal,
                BankCash   = (decimal)bankCash,
                CashInHand = (decimal)cashInHand,
                Expenses   = (decimal)expensesTotal,
                Total      = (decimal)reconciliationTotal,
                TotalDsmShort = (decimal)totalDsmShort
            };

            return new FinalCalcPrintData
            {
                Date               = date.ToString("dd/MM/yyyy"),
                ShiftLabel         = shiftLabel,
                StationName        = stationName,
                DsmEntries         = dsmPrintRows,
                Cash1              = c1,
                Cash2              = c2,
                Creditors          = creditorPrintRows,
                FuelSale           = fuelRows,
                OtherCash          = (decimal)otherCashTotal,
                Cheque             = 0m,
                MsILitres          = (decimal)msILitres,
                MsIILitres         = (decimal)msIILitres,
                CngLitres          = (decimal)cngLitres,
                GrossFuelSaleTotal = (decimal)grossFuelSaleTotal,
                Expenses           = expensePrintRows,
                Reconciliation     = rec,
                SameDayRepayments  = (sameDayRepayments ?? new List<CreditorRepayment>()).Select(r => new CreditorRepaymentPrintDto
                {
                    DebtorName = r.CreditorName,
                    PaymentMode = r.PaymentMode,
                    RefNo = r.ChequeNo ?? "",
                    Amount = (decimal)r.Amount
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "BuildPrintData failed");
            throw;
        }
    }
}
