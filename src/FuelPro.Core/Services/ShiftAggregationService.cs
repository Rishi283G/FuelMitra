using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using Serilog;

namespace FuelPro.Core.Services;

/// <summary>
/// Aggregates all DSM entry data for a shift into the structures needed
/// by each table on the Final Calculation page.
/// </summary>
public class ShiftAggregationService : IShiftAggregationService
{
    private readonly ILogger _logger = Log.ForContext<ShiftAggregationService>();

    /// <summary>
    /// TABLE A — One row per DSM entry with all payment/cash/expense columns.
    /// </summary>
    public List<DsmSummaryRowDto> BuildDsmSummaryRows(List<DsmEntry> entries)
    {
        try
        {
            var rows = new List<DsmSummaryRowDto>();
            var primaryEntries = entries.Where(e => !e.ReconciledToPumpId.HasValue).ToList();
            foreach (var entry in primaryEntries)
            {
                var connected = entries.FirstOrDefault(e => e.ReconciledToPumpId == entry.DsmEntryId || 
                    (entry.ConnectedPumpId.HasValue && e.PumpId == entry.ConnectedPumpId.Value && e.ShiftId == entry.ShiftId && e.DsmEntryId != entry.DsmEntryId));

                var allNozzleReadings = new List<NozzleReading>();
                if (entry.NozzleReadings != null) allNozzleReadings.AddRange(entry.NozzleReadings);
                if (connected?.NozzleReadings != null) allNozzleReadings.AddRange(connected.NozzleReadings);

                var allCashDenoms = new List<CashDenomination>();
                if (entry.CashDenominations != null) allCashDenoms.AddRange(entry.CashDenominations);
                if (connected?.CashDenominations != null) allCashDenoms.AddRange(connected.CashDenominations);

                var cash1 = allCashDenoms.Where(c => c.CashType == "Cash1").Sum(c => c.TotalAmount);
                var cash2 = allCashDenoms.Where(c => c.CashType == "Cash2").Sum(c => c.TotalAmount);

                var debSum = entry.DebitEntries.Sum(d => d.Amount) + (connected?.DebitEntries.Sum(d => d.Amount) ?? 0);
                var totalDebit = debSum > 0 ? debSum : (double)(entry.TotalCreditors + (connected?.TotalCreditors ?? 0));

                var expSum = entry.Expenses.Sum(e => e.Amount) + (connected?.Expenses.Sum(e => e.Amount) ?? 0);
                var kpSum = (entry.KhandharePetroleumEntries != null ? entry.KhandharePetroleumEntries.Sum(kp => kp.Amount) : 0)
                    + (connected?.KhandharePetroleumEntries != null ? connected.KhandharePetroleumEntries.Sum(kp => kp.Amount) : 0);
                var totalExpenses = expSum + kpSum;
                var totalTesting = entry.TestingEntries.Sum(t => t.Amount) + (connected?.TestingEntries.Sum(t => t.Amount) ?? 0);

                var p1 = entry.PaymentCollection;
                var p2 = connected?.PaymentCollection;

                double qrSum = (entry.QrPayments?.Sum(q => q.Amount) ?? 0) + (connected?.QrPayments?.Sum(q => q.Amount) ?? 0);
                double qrM = (entry.QrPayments?.Where(q => q.Slot == "Morning" || q.Slot == "Day" || string.IsNullOrEmpty(q.Slot)).Sum(q => q.Amount) ?? 0)
                           + (connected?.QrPayments?.Where(q => q.Slot == "Morning" || q.Slot == "Day" || string.IsNullOrEmpty(q.Slot)).Sum(q => q.Amount) ?? 0);
                double qrN = (entry.QrPayments?.Where(q => q.Slot == "Night").Sum(q => q.Amount) ?? 0)
                           + (connected?.QrPayments?.Where(q => q.Slot == "Night").Sum(q => q.Amount) ?? 0);

                double phM = (p1?.PhonePeMorning ?? 0) + (p2?.PhonePeMorning ?? 0) + qrM;
                double phD = (p1?.PhonePeDay ?? 0) + (p2?.PhonePeDay ?? 0) + qrM;
                double phN = (p1?.PhonePeNight ?? 0) + (p2?.PhonePeNight ?? 0) + qrN;
                double ph = (p1?.PhonePe ?? 0) + (p2?.PhonePe ?? 0) + qrSum;

                double ppcM = (p1?.PhonePeCardMorning ?? 0) + (p2?.PhonePeCardMorning ?? 0);
                double ppcD = (p1?.PhonePeCardDay ?? 0) + (p2?.PhonePeCardDay ?? 0);
                double ppcN = (p1?.PhonePeCardNight ?? 0) + (p2?.PhonePeCardNight ?? 0);
                double ppc = (p1?.PhonePeCard ?? 0) + (p2?.PhonePeCard ?? 0);

                double ccM = (p1?.CreditCardMorning ?? 0) + (p2?.CreditCardMorning ?? 0);
                double ccD = (p1?.CreditCardDay ?? 0) + (p2?.CreditCardDay ?? 0);
                double ccN = (p1?.CreditCardNight ?? 0) + (p2?.CreditCardNight ?? 0);

                double petroM = (p1?.PetroCardMorning ?? 0) + (p2?.PetroCardMorning ?? 0);
                double petroD = (p1?.PetroCardDay ?? 0) + (p2?.PetroCardDay ?? 0);
                double petroN = (p1?.PetroCardNight ?? 0) + (p2?.PetroCardNight ?? 0);
                double petro = (p1?.PetroCard ?? 0) + (p2?.PetroCard ?? 0);

                var distinctNozzles = allNozzleReadings
                    .GroupBy(n => n.NozzleNumber)
                    .Select(g => g.First())
                    .ToList();

                double grossSales = distinctNozzles.Count > 0
                    ? distinctNozzles.Sum(n => (double)n.Amount)
                    : (double)entry.GrossSales;

                rows.Add(new DsmSummaryRowDto
                {
                    DsmName = entry.DsmName,
                    Shift = entry.Shift?.ShiftType ?? "",
                    PumpId = entry.PumpId,
                    ConnectedPumpId = entry.ConnectedPumpId,
                    PhonePeCard = ppc > 0 ? ppc : (ppcM + ppcD + ppcN),
                    PhonePeCardMorning = ppcM > 0 ? ppcM : ppcD,
                    PhonePeCardDay = ppcD,
                    PhonePeCardNight = ppcN,
                    PhonePe = ph > 0 ? ph : (phM + phD + phN),
                    PhonePeMorning = phM > 0 ? phM : (phD > 0 ? phD : ph),
                    PhonePeDay = phD,
                    PhonePeNight = phN,
                    CreditCardMorning = ccM > 0 ? ccM : ccD,
                    CreditCardDay = ccD,
                    CreditCardNight = ccN,
                    PetroCard = petro > 0 ? petro : (petroM + petroD + petroN),
                    PetroCardMorning = petroM > 0 ? petroM : (petroD > 0 ? petroD : petro),
                    PetroCardDay = petroD,
                    PetroCardNight = petroN,
                    Others = (p1?.Others ?? 0) + (p2?.Others ?? 0),
                    CashDeposit = cash1 > 0 ? cash1 : ((p1?.CashDeposit ?? 0) + (p2?.CashDeposit ?? 0)),
                    Debit = totalDebit,
                    Expenses = totalExpenses,
                    Testing = totalTesting,
                    CashInHand = cash2,
                    GrossSales = grossSales
                });
            }
            return rows;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to build DSM summary rows");
            return new List<DsmSummaryRowDto>();
        }
    }

    /// <summary>
    /// Builds the TOTAL row for Table A (column-wise sums).
    /// </summary>
    public DsmSummaryRowDto BuildDsmSummaryTotalRow(List<DsmSummaryRowDto> rows)
    {
        return new DsmSummaryRowDto
        {
            DsmName = "TOTAL",
            PumpId = 0,
            PhonePeCard = rows.Sum(r => r.PhonePeCard),
            PhonePeCardMorning = rows.Sum(r => r.PhonePeCardMorning),
            PhonePeCardNight = rows.Sum(r => r.PhonePeCardNight),
            PhonePe = rows.Sum(r => r.PhonePeTotal),
            PhonePeMorning = rows.Sum(r => r.PhonePeMorning),
            PhonePeNight = rows.Sum(r => r.PhonePeNight),
            CreditCardMorning = rows.Sum(r => r.CreditCardMorning),
            CreditCardNight = rows.Sum(r => r.CreditCardNight),
            PetroCard = rows.Sum(r => r.PetroCardTotal),
            PetroCardMorning = rows.Sum(r => r.PetroCardMorning),
            PetroCardNight = rows.Sum(r => r.PetroCardNight),
            Others = rows.Sum(r => r.Others),
            CashDeposit = rows.Sum(r => r.CashDeposit),
            Debit = rows.Sum(r => r.Debit),
            Expenses = rows.Sum(r => r.Expenses),
            Testing = rows.Sum(r => r.Testing),
            CashInHand = rows.Sum(r => r.CashInHand),
            GrossSales = rows.Sum(r => r.GrossSales)
        };
    }

    /// <summary>
    /// Aggregates individual DSM summary rows by DSM Name for shift-level DSM totals.
    /// </summary>
    public List<DsmShiftTotalDto> BuildDsmShiftTotals(List<DsmSummaryRowDto> rows)
    {
        if (rows == null || rows.Count == 0) return new List<DsmShiftTotalDto>();

        return rows
            .GroupBy(r => (r.DsmName ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var dsmName = g.Key;
                var pumpsList = g.Select(r => r.PumpLabel).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
                var pumpsDisplay = string.Join(", ", pumpsList);

                double grossSales = g.Sum(r => r.GrossSales);
                double cashDeposit = g.Sum(r => r.CashDeposit);
                double cashInHand = g.Sum(r => r.CashInHand);
                double phonePe = g.Sum(r => r.PhonePeTotal);
                double phonePeCard = g.Sum(r => r.PhonePeCardTotal);
                double creditCard = g.Sum(r => r.CreditCardTotal);
                double petroCard = g.Sum(r => r.PetroCardTotal);
                double others = g.Sum(r => r.Others);
                double debit = g.Sum(r => r.Debit);
                double expenses = g.Sum(r => r.Expenses);
                double testing = g.Sum(r => r.Testing);

                double totalCollection = cashDeposit + cashInHand + phonePe + phonePeCard + creditCard + petroCard + debit + expenses + testing;
                double mismatch = totalCollection - grossSales;

                return new DsmShiftTotalDto
                {
                    DsmName = dsmName,
                    SessionsCount = g.Count(),
                    AssignedPumpsDisplay = pumpsDisplay,
                    GrossSales = grossSales,
                    TotalCollection = totalCollection,
                    CashDeposit = cashDeposit,
                    CashInHand = cashInHand,
                    PhonePe = phonePe,
                    PhonePeCard = phonePeCard,
                    CreditCard = creditCard,
                    PetroCard = petroCard,
                    Debit = debit,
                    Expenses = expenses,
                    Testing = testing,
                    Mismatch = mismatch
                };
            })
            .OrderBy(s => s.DsmName)
            .ToList();
    }

    /// <summary>
    /// TABLE B — Aggregates cash denomination counts across all DSMs for a given cash type.
    /// For Cash1 (Bank Deposit), the CashDeposit amount from the payment collection is also included.
    /// </summary>
    public CashAggregateDto AggregateCash(List<DsmEntry> entries, string cashType)
    {
        try
        {
            var denoms = entries
                .SelectMany(e => e.CashDenominations)
                .Where(c => c.CashType == cashType)
                .ToList();

            var dto = new CashAggregateDto
            {
                Total500 = denoms.Sum(d => d.Denom500),
                Total200 = denoms.Sum(d => d.Denom200),
                Total100 = denoms.Sum(d => d.Denom100),
                Total50  = cashType == "Cash1" ? 0 : denoms.Sum(d => d.Denom50),
                Total20  = cashType == "Cash1" ? 0 : denoms.Sum(d => d.Denom20),
                Total10  = cashType == "Cash1" ? 0 : denoms.Sum(d => d.Denom10),
                TotalCoins = cashType == "Cash1" ? 0 : denoms.Sum(d => d.Coins)
            };

            double physicalTotal = denoms.Sum(d => d.TotalAmount);

            // For Cash1 (Bank Deposit), add the CashDeposit from each DSM's payment collection
            if (cashType == "Cash1")
            {
                dto.CashDepositTotal = entries.Sum(e => e.PaymentCollection?.CashDeposit ?? 0);
                dto.GrandTotal = physicalTotal > 0 ? physicalTotal : dto.CashDepositTotal;
            }
            else
            {
                dto.GrandTotal = physicalTotal;
            }
            return dto;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to aggregate cash for type {CashType}", cashType);
            return new CashAggregateDto();
        }
    }

    /// <summary>
    /// TABLE C — Flat list of all debit/creditor entries across all DSMs.
    /// </summary>
    public List<DebitRegisterRowDto> BuildCreditorRows(List<DsmEntry> entries)
    {
        try
        {
            var rows = new List<DebitRegisterRowDto>();
            foreach (var entry in entries)
            {
                if (entry.DebitEntries != null && entry.DebitEntries.Count > 0)
                {
                    foreach (var debit in entry.DebitEntries)
                    {
                        rows.Add(new DebitRegisterRowDto
                        {
                            DsmName = entry.DsmName,
                            PumpId = entry.PumpId,
                            DebtorName = debit.DebtorName,
                            Amount = debit.Amount,
                            ChequeNo = debit.ChequeNo
                        });
                    }
                }
                else if (entry.TotalCreditors > 0 && !entry.ReconciledToPumpId.HasValue)
                {
                    rows.Add(new DebitRegisterRowDto
                    {
                        DsmName = entry.DsmName,
                        PumpId = entry.PumpId,
                        DebtorName = "Debtors Total",
                        Amount = (double)entry.TotalCreditors,
                        ChequeNo = ""
                    });
                }
            }
            return rows.OrderBy(r => r.DsmName).ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to build creditor rows");
            return new List<DebitRegisterRowDto>();
        }
    }

    /// <summary>
    /// TABLE D — All expenses: DSM-level (read-only) + shift-level (editable).
    /// </summary>
    public List<ExpenseRegisterRowDto> BuildExpenseRows(List<DsmEntry> entries, List<Expense> shiftExpenses)
    {
        try
        {
            var rows = new List<ExpenseRegisterRowDto>();

            // DSM-level expenses & drawings
            foreach (var entry in entries)
            {
                foreach (var expense in entry.Expenses)
                {
                    rows.Add(new ExpenseRegisterRowDto
                    {
                        ExpenseId = expense.ExpenseId,
                        DsmName = entry.DsmName,
                        PumpId = entry.PumpId,
                        Description = expense.Description,
                        Amount = expense.Amount,
                        IsShiftLevel = false,
                        CanDelete = false
                    });
                }
            }

            // Shift-level expenses
            foreach (var expense in shiftExpenses)
            {
                rows.Add(new ExpenseRegisterRowDto
                {
                    ExpenseId = expense.ExpenseId,
                    DsmName = "— Shift —",
                    PumpId = 0,
                    Description = expense.Description,
                    Amount = expense.Amount,
                    IsShiftLevel = true,
                    CanDelete = true
                });
            }

            return rows;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to build expense rows");
            return new List<ExpenseRegisterRowDto>();
        }
    }

    /// <summary>
    /// TABLE E — Calculates total litres and amount for a fuel type from all DSM nozzle readings.
    /// Classifies each reading by PumpConfiguration (nozzle number) rather than the stored
    /// FuelType string, so old entries with stale labels are handled correctly.
    /// Optionally applies an override rate; if null, uses the rate stored on each reading.
    /// </summary>
    public (double litres, double amount) GetFuelTotals(List<DsmEntry> entries, string fuelType, double? overrideRate)
    {
        try
        {
            var shiftDate = entries.FirstOrDefault()?.Shift?.ShiftDate;
            var readings = entries
                .SelectMany(e => (e.NozzleReadings ?? new List<NozzleReading>()).Select(r => new { ShiftId = e.ShiftId, e.PumpId, Reading = r }))
                .Where(x => PumpConfiguration.GetFuelTypeDisplayName(x.PumpId, x.Reading.NozzleNumber, shiftDate) == fuelType)
                .Select(x => x.Reading)
                .ToList();

            var litres = readings.Sum(r => r.SaleLitres);

            double amount;
            if (overrideRate.HasValue && overrideRate.Value > 0)
            {
                amount = litres * overrideRate.Value;
            }
            else
            {
                amount = readings.Sum(r => r.Amount);
            }

            return (litres, amount);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get fuel totals for {FuelType}", fuelType);
            return (0, 0);
        }
    }

    /// <summary>
    /// Returns total litres dispensed for a given fuel type across all readings.
    /// </summary>
    public double GetTotalLitresByFuelType(List<NozzleReading> allReadings, string fuelType)
    {
        return allReadings
            .Where(r => 
            {
                int pumpId = r.DsmEntry?.PumpId ?? 0;
                if (pumpId == 0)
                {
                    var match = PumpConfiguration.PumpNozzleMapping.FirstOrDefault(kv => kv.Value.Contains(r.NozzleNumber));
                    pumpId = match.Key;
                }
                return PumpConfiguration.GetFuelTypeDisplayName(pumpId, r.NozzleNumber, r.DsmEntry?.Shift?.ShiftDate) == fuelType;
            }).Sum(r => r.SaleLitres);
    }

    /// <summary>
    /// TABLE F — Builds reconciliation line items.
    /// </summary>
    public List<ReconciliationRowDto> BuildReconciliationRows(
        double msTesting, double hsdTesting, double hsdTesting2, double cngTesting, double phonePeCardMorning, double phonePeCardNight, double phonePeMorning, double phonePeNight, double petroCard,
        double debit, double creditCardMorning, double creditCardNight, double bankCash, double cashInHand,
        double expenses)
    {
        return new List<ReconciliationRowDto>
        {
            new() { Description = "MS Testing", Amount = msTesting },
            new() { Description = "HSD Testing I", Amount = hsdTesting },
            new() { Description = "HSD Testing II", Amount = hsdTesting2 },
            new() { Description = "Phone Pe (Morning)", Amount = phonePeMorning },
            new() { Description = "Phone Pe (Night)", Amount = phonePeNight },
            new() { Description = "P. Card", Amount = petroCard },
            new() { Description = "Debit", Amount = debit },
            new() { Description = "Card (Morning)", Amount = creditCardMorning },
            new() { Description = "Card (Night)", Amount = creditCardNight },
            new() { Description = "Bank Cash", Amount = bankCash },
            new() { Description = "Cash In Hand", Amount = cashInHand },
            new() { Description = "Expenses", Amount = expenses }
        };
    }

    /// <summary>
    /// Calculates the difference between gross fuel sale and reconciliation total.
    /// Positive = short, Negative = excess, Zero = balanced.
    /// </summary>
    public double CalculateDifference(double grossSaleFuel, double reconciliationTotal)
    {
        return reconciliationTotal - grossSaleFuel;
    }
}
