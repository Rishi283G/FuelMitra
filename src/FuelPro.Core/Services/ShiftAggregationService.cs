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
            var primaryEntries = entries.Where(e => !e.ReconciledToPumpId.HasValue).OrderBy(e => e.DsmEntryId).ToList();
            var slaveEntries = entries.Where(e => e.ReconciledToPumpId.HasValue).ToList();
            var usedSlaveIds = new HashSet<int>();

            foreach (var entry in primaryEntries)
            {
                var primaryConnectedPumps = entry.GetEffectiveConnectedPumpIds();
                var connectedSlaves = new List<DsmEntry>();

                foreach (var slavePumpId in primaryConnectedPumps)
                {
                    var candidates = slaveEntries
                        .Where(s => !usedSlaveIds.Contains(s.DsmEntryId)
                            && s.PumpId == slavePumpId
                            && (s.ShiftId == entry.ShiftId || entry.ShiftId == 0 || s.ShiftId == 0))
                        .ToList();

                    var bestSlave = candidates
                        .OrderBy(s => {
                            bool nameMatches = string.IsNullOrWhiteSpace(s.DsmName) || string.IsNullOrWhiteSpace(entry.DsmName)
                                || string.Equals((s.DsmName ?? "").Trim(), (entry.DsmName ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
                            bool explicitRecon = s.ReconciledToPumpId == entry.DsmEntryId;
                            bool notClaimed = !s.ReconciledToPumpId.HasValue || !primaryEntries.Any(p => p.DsmEntryId == s.ReconciledToPumpId.Value && p.DsmEntryId != entry.DsmEntryId);

                            if (nameMatches && explicitRecon) return 0;
                            if (nameMatches && notClaimed) return 1;
                            if (nameMatches) return 2;
                            if (explicitRecon) return 3;
                            if (notClaimed) return 4;
                            return 5;
                        })
                        .ThenBy(s => s.DsmEntryId >= entry.DsmEntryId ? (s.DsmEntryId - entry.DsmEntryId) : (100000 + Math.Abs(s.DsmEntryId - entry.DsmEntryId)))
                        .FirstOrDefault();

                    if (bestSlave != null)
                    {
                        usedSlaveIds.Add(bestSlave.DsmEntryId);
                        connectedSlaves.Add(bestSlave);
                    }
                }

                // Fuel/nozzle sales: aggregate primary + ALL connected slaves
                var allNozzleReadings = new List<NozzleReading>();
                if (entry.NozzleReadings != null) allNozzleReadings.AddRange(entry.NozzleReadings);
                foreach (var slave in connectedSlaves)
                {
                    if (slave.NozzleReadings != null) allNozzleReadings.AddRange(slave.NozzleReadings);
                }

                var distinctNozzles = allNozzleReadings
                    .GroupBy(n => n.NozzleNumber)
                    .Select(g => g.First())
                    .ToList();

                double grossSales = distinctNozzles.Count > 0
                    ? distinctNozzles.Sum(n => (double)n.Amount)
                    : (entry.GrossSales > 0 ? (double)entry.GrossSales : (double)(entry.GrossSales + connectedSlaves.Sum(s => s.GrossSales)));

                // Financial collections: PRIMARY ONLY per Phase 3 rules, with graceful fallback to slave if primary is empty
                var primaryCashDenoms = entry.CashDenominations ?? new List<CashDenomination>();
                var cash1 = primaryCashDenoms.Where(c => c.CashType == "Cash1").Sum(c => c.TotalAmount);
                var cash2 = primaryCashDenoms.Where(c => c.CashType == "Cash2").Sum(c => c.TotalAmount);
                if (cash1 == 0 && cash2 == 0 && connectedSlaves.Any(s => s.CashDenominations != null && s.CashDenominations.Count > 0))
                {
                    var slaveCashDenoms = connectedSlaves.SelectMany(s => s.CashDenominations ?? new List<CashDenomination>()).ToList();
                    cash1 = slaveCashDenoms.Where(c => c.CashType == "Cash1").Sum(c => c.TotalAmount);
                    cash2 = slaveCashDenoms.Where(c => c.CashType == "Cash2").Sum(c => c.TotalAmount);
                }

                var debSum = entry.DebitEntries?.Sum(d => d.Amount) ?? 0;
                if (debSum == 0 && connectedSlaves.Any(s => s.DebitEntries != null && s.DebitEntries.Count > 0))
                {
                    debSum = connectedSlaves.SelectMany(s => s.DebitEntries ?? new List<DebitEntry>()).Sum(d => d.Amount);
                }
                var totalDebit = debSum > 0 ? debSum : (double)entry.TotalCreditors;

                var expSum = entry.Expenses?.Sum(e => e.Amount) ?? 0;
                if (expSum == 0 && connectedSlaves.Any(s => s.Expenses != null && s.Expenses.Count > 0))
                {
                    expSum = connectedSlaves.SelectMany(s => s.Expenses ?? new List<Expense>()).Sum(e => e.Amount);
                }
                var kpSum = entry.KhandharePetroleumEntries != null ? entry.KhandharePetroleumEntries.Sum(kp => kp.Amount) : 0;
                if (kpSum == 0 && connectedSlaves.Any(s => s.KhandharePetroleumEntries != null && s.KhandharePetroleumEntries.Count > 0))
                {
                    kpSum = connectedSlaves.SelectMany(s => s.KhandharePetroleumEntries ?? new List<KhandharePetroleumEntry>()).Sum(kp => kp.Amount);
                }
                var totalExpenses = expSum + kpSum;

                var allTestingList = new List<TestingEntry>();
                if (entry.TestingEntries != null) allTestingList.AddRange(entry.TestingEntries);
                foreach (var slave in connectedSlaves)
                {
                    if (slave.TestingEntries != null) allTestingList.AddRange(slave.TestingEntries);
                }
                var distinctTesting = allTestingList
                    .GroupBy(t => t.TestingId > 0 ? t.TestingId.ToString() : $"{t.DsmEntryId}_{t.FuelType}_{t.Litres}_{t.Amount}")
                    .Select(g => g.First())
                    .ToList();
                var totalTesting = distinctTesting.Sum(t => t.Amount);

                var p1 = entry.PaymentCollection ?? connectedSlaves.FirstOrDefault(s => s.PaymentCollection != null)?.PaymentCollection;

                bool isDay = string.Equals(entry.Shift?.ShiftType, "B", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(entry.Shift?.ShiftType, "II", StringComparison.OrdinalIgnoreCase);

                double qrSum = entry.QrPayments?.Sum(q => q.Amount) ?? 0;
                double qrM = entry.QrPayments?.Where(q => q.Slot == "Morning" || (isDay && q.Slot == "Day") || string.IsNullOrEmpty(q.Slot)).Sum(q => q.Amount) ?? 0;
                double qrN = entry.QrPayments?.Where(q => q.Slot == "Night").Sum(q => q.Amount) ?? 0;

                double phM = p1?.PhonePeMorning ?? 0;
                double phD = p1?.PhonePeDay ?? 0;
                double phN = p1?.PhonePeNight ?? 0;
                double ph = p1?.PhonePe ?? 0;

                double ppcM = p1?.PhonePeCardMorning ?? 0;
                double ppcD = p1?.PhonePeCardDay ?? 0;
                double ppcN = p1?.PhonePeCardNight ?? 0;
                double ppc = p1?.PhonePeCard ?? 0;

                double ccM = p1?.CreditCardMorning ?? 0;
                double ccD = p1?.CreditCardDay ?? 0;
                double ccN = p1?.CreditCardNight ?? 0;
                double cc = p1?.CreditCard ?? 0;

                double petroM = p1?.PetroCardMorning ?? 0;
                double petroD = p1?.PetroCardDay ?? 0;
                double petroN = p1?.PetroCardNight ?? 0;
                double petro = p1?.PetroCard ?? 0;

                double finalPhM = isDay ? 0 : (phM > 0 ? phM + qrM : 0);
                double finalPhD = isDay ? ((phD > 0 ? phD : ph) + qrSum) : phD;
                double finalPhN = isDay ? 0 : (phN > 0 ? phN + qrN : 0);
                double finalPh = isDay ? finalPhD : ((ph > 0 ? ph + qrSum : (finalPhM + finalPhN)));

                double finalPpcM = isDay ? 0 : ppcM;
                double finalPpcD = isDay ? (ppcD > 0 ? ppcD : ppc) : ppcD;
                double finalPpcN = isDay ? 0 : ppcN;
                double finalPpc = isDay ? finalPpcD : (ppc > 0 ? ppc : (finalPpcM + finalPpcN));

                double finalCcM = isDay ? 0 : ccM;
                double finalCcD = isDay ? (ccD > 0 ? ccD : cc) : ccD;
                double finalCcN = isDay ? 0 : ccN;
                double finalCc = isDay ? finalCcD : (cc > 0 ? cc : (finalCcM + finalCcN));

                double finalPetroM = isDay ? 0 : petroM;
                double finalPetroD = isDay ? (petroD > 0 ? petroD : petro) : petroD;
                double finalPetroN = isDay ? 0 : petroN;
                double finalPetro = isDay ? finalPetroD : (petro > 0 ? petro : (finalPetroM + finalPetroN));

                var allItems = p1?.Items?.ToList() ?? new List<PaymentCollectionItem>();

                double sbiRedeem = allItems.Where(i => string.Equals(i.CollectionTypeCode?.Replace("_", "")?.Replace(" ", ""), "SBIREDEEM", StringComparison.OrdinalIgnoreCase)).Sum(i => i.Amount);
                double paytm = allItems.Where(i => string.Equals(i.CollectionTypeCode, "PAYTM", StringComparison.OrdinalIgnoreCase)).Sum(i => i.Amount);
                double qrPayment = allItems.Where(i => string.Equals(i.CollectionTypeCode, "QR", StringComparison.OrdinalIgnoreCase) || string.Equals(i.CollectionTypeCode?.Replace("_", "")?.Replace(" ", ""), "QRONLINE", StringComparison.OrdinalIgnoreCase)).Sum(i => i.Amount);
                double mobikwik = allItems.Where(i => string.Equals(i.CollectionTypeCode, "MOBIKWIK", StringComparison.OrdinalIgnoreCase) || string.Equals(i.CollectionTypeCode, "MOBIKWICK", StringComparison.OrdinalIgnoreCase)).Sum(i => i.Amount);
                double dynTotal = allItems.Sum(i => i.Amount);

                var dynDict = allItems
                    .GroupBy(i => i.CollectionTypeCode, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount), StringComparer.OrdinalIgnoreCase);

                rows.Add(new DsmSummaryRowDto
                {
                    DsmName = entry.DsmName,
                    Shift = entry.Shift?.ShiftType ?? "",
                    PumpId = entry.PumpId,
                    ConnectedPumpId = entry.ConnectedPumpId ?? connectedSlaves.FirstOrDefault()?.PumpId,
                    ConnectedPumpIdsJson = entry.ConnectedPumpIdsJson ?? (connectedSlaves.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(connectedSlaves.Select(s => s.PumpId).Distinct()) : null),
                    PhonePeCard = finalPpc,
                    PhonePeCardMorning = finalPpcM,
                    PhonePeCardDay = finalPpcD,
                    PhonePeCardNight = finalPpcN,
                    PhonePe = finalPh,
                    PhonePeMorning = finalPhM,
                    PhonePeDay = finalPhD,
                    PhonePeNight = finalPhN,
                    CreditCard = finalCc,
                    CreditCardMorning = finalCcM,
                    CreditCardDay = finalCcD,
                    CreditCardNight = finalCcN,
                    PetroCard = finalPetro,
                    PetroCardMorning = finalPetroM,
                    PetroCardDay = finalPetroD,
                    PetroCardNight = finalPetroN,
                    Others = p1?.Others ?? 0,
                    DynamicCollectionsTotal = dynTotal,
                    DynamicCollections = dynDict,
                    SbiRedeem = sbiRedeem,
                    Paytm = paytm,
                    QrPayment = qrPayment,
                    Mobikwik = mobikwik,
                    CashDeposit = cash1 > 0 ? cash1 : (p1?.CashDeposit ?? 0),
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
        var totalDynDict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            if (r.DynamicCollections != null)
            {
                foreach (var kvp in r.DynamicCollections)
                {
                    totalDynDict[kvp.Key] = (totalDynDict.TryGetValue(kvp.Key, out var val) ? val : 0) + kvp.Value;
                }
            }
        }

        return new DsmSummaryRowDto
        {
            DsmName = "TOTAL",
            PumpId = 0,
            PhonePeCard = rows.Sum(r => r.PhonePeCardTotal),
            PhonePeCardMorning = rows.Sum(r => r.PhonePeCardMorning),
            PhonePeCardDay = rows.Sum(r => r.PhonePeCardDay),
            PhonePeCardNight = rows.Sum(r => r.PhonePeCardNight),
            PhonePe = rows.Sum(r => r.PhonePeTotal),
            PhonePeMorning = rows.Sum(r => r.PhonePeMorning),
            PhonePeDay = rows.Sum(r => r.PhonePeDay),
            PhonePeNight = rows.Sum(r => r.PhonePeNight),
            CreditCard = rows.Sum(r => r.CreditCardPureTotal),
            CreditCardMorning = rows.Sum(r => r.CreditCardMorning),
            CreditCardDay = rows.Sum(r => r.CreditCardDay),
            CreditCardNight = rows.Sum(r => r.CreditCardNight),
            PetroCard = rows.Sum(r => r.PetroCardTotal),
            PetroCardMorning = rows.Sum(r => r.PetroCardMorning),
            PetroCardDay = rows.Sum(r => r.PetroCardDay),
            PetroCardNight = rows.Sum(r => r.PetroCardNight),
            Others = rows.Sum(r => r.Others),
            DynamicCollectionsTotal = rows.Sum(r => r.DynamicCollectionsTotal),
            DynamicCollections = totalDynDict,
            SbiRedeem = rows.Sum(r => r.SbiRedeem),
            Paytm = rows.Sum(r => r.Paytm),
            QrPayment = rows.Sum(r => r.QrPayment),
            Mobikwik = rows.Sum(r => r.Mobikwik),
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
                double creditCard = g.Sum(r => r.CreditCardPureTotal);
                double petroCard = g.Sum(r => r.PetroCardTotal);
                double others = g.Sum(r => r.Others);
                double debit = g.Sum(r => r.Debit);
                double expenses = g.Sum(r => r.Expenses);
                double testing = g.Sum(r => r.Testing);

                double dynamicTotal = g.Sum(r => r.DynamicCollectionsTotal);
                var dynDict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in g)
                {
                    if (r.DynamicCollections != null)
                    {
                        foreach (var kvp in r.DynamicCollections)
                        {
                            dynDict[kvp.Key] = (dynDict.TryGetValue(kvp.Key, out var existing) ? existing : 0) + kvp.Value;
                        }
                    }
                }

                double netGrossSales = grossSales - testing;
                double totalCollection = cashDeposit + cashInHand + phonePe + phonePeCard + creditCard + petroCard + dynamicTotal + debit + expenses;
                double mismatch = totalCollection - netGrossSales;

                return new DsmShiftTotalDto
                {
                    DsmName = dsmName,
                    SessionsCount = g.Count(),
                    AssignedPumpsDisplay = pumpsDisplay,
                    GrossSales = grossSales,
                    TotalCollection = totalCollection,
                    CashDeposit = cashDeposit,
                    CashInHand = cashInHand,
                    Others = others,
                    PhonePe = phonePe,
                    PhonePeCard = phonePeCard,
                    CreditCard = creditCard,
                    PetroCard = petroCard,
                    DynamicCollectionsTotal = dynamicTotal,
                    DynamicCollections = dynDict,
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
            var isCngTarget = string.Equals(fuelType, "CNG", StringComparison.OrdinalIgnoreCase) || fuelType.Contains("CNG", StringComparison.OrdinalIgnoreCase);

            var readings = entries
                .SelectMany(e => (e.NozzleReadings ?? new List<NozzleReading>()).Select(r => new { ShiftId = e.ShiftId, e.PumpId, Reading = r, ShiftDate = e.Shift?.ShiftDate ?? shiftDate }))
                .Where(x => 
                {
                    var canon = PumpConfiguration.GetFuelTypeDisplayName(x.PumpId, x.Reading.NozzleNumber, x.ShiftDate);
                    if (string.Equals(canon, fuelType, StringComparison.OrdinalIgnoreCase)) return true;
                    if (isCngTarget)
                    {
                        if (canon.Contains("CNG", StringComparison.OrdinalIgnoreCase)) return true;
                        if (x.Reading.FuelType != null && x.Reading.FuelType.Contains("CNG", StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    return false;
                })
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
                if (amount == 0 && litres > 0 && overrideRate.HasValue)
                {
                    amount = litres * overrideRate.Value;
                }
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
        var isCngTarget = string.Equals(fuelType, "CNG", StringComparison.OrdinalIgnoreCase) || fuelType.Contains("CNG", StringComparison.OrdinalIgnoreCase);
        return allReadings
            .Where(r => 
            {
                int pumpId = r.DsmEntry?.PumpId ?? 0;
                if (pumpId == 0)
                {
                    var match = PumpConfiguration.PumpNozzleMapping.FirstOrDefault(kv => kv.Value.Contains(r.NozzleNumber));
                    pumpId = match.Key;
                }
                var canon = PumpConfiguration.GetFuelTypeDisplayName(pumpId, r.NozzleNumber, r.DsmEntry?.Shift?.ShiftDate);
                if (string.Equals(canon, fuelType, StringComparison.OrdinalIgnoreCase)) return true;
                if (isCngTarget)
                {
                    if (canon.Contains("CNG", StringComparison.OrdinalIgnoreCase)) return true;
                    if (r.FuelType != null && r.FuelType.Contains("CNG", StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }).Sum(r => r.SaleLitres);
    }

    /// <summary>
    /// TABLE F — Builds reconciliation line items.
    /// </summary>
    public List<ReconciliationRowDto> BuildReconciliationRows(
        double msTesting, double hsdTesting, double hsdTesting2, double cngTesting, double phonePeCardMorning, double phonePeCardNight, double phonePeMorning, double phonePeNight, double petroCard,
        double debit, double creditCardMorning, double creditCardNight, double bankCash, double cashInHand,
        double expenses, Dictionary<string, double>? dynamicCollections = null, double others = 0)
    {
        var rows = new List<ReconciliationRowDto>
        {
            new() { Description = "Bank Cash", Amount = bankCash },
            new() { Description = "Cash In Hand", Amount = cashInHand },
            new() { Description = "PhonePe", Amount = phonePeMorning + phonePeNight },
            new() { Description = "PineLab Card", Amount = creditCardMorning + creditCardNight + phonePeCardMorning + phonePeCardNight },
            new() { Description = "PetroCard", Amount = petroCard },
            new() { Description = "Debtors", Amount = debit },
            new() { Description = "Expenses", Amount = expenses }
        };

        if (msTesting > 0) rows.Add(new() { Description = "MS Testing", Amount = msTesting });
        if (hsdTesting > 0) rows.Add(new() { Description = "HSD Testing I", Amount = hsdTesting });
        if (hsdTesting2 > 0) rows.Add(new() { Description = "HSD Testing II", Amount = hsdTesting2 });
        if (cngTesting > 0) rows.Add(new() { Description = "CNG Testing", Amount = cngTesting });

        if (dynamicCollections != null)
        {
            foreach (var kvp in dynamicCollections.Where(k => k.Value > 0 &&
                !string.Equals(k.Key?.Replace("_", "").Replace(" ", ""), "CASHDEPOSIT", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(k.Key?.Replace("_", "").Replace(" ", ""), "BANKCASH", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(k.Key?.Replace("_", "").Replace(" ", ""), "CASHINHAND", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(k.Key?.Replace("_", "").Replace(" ", ""), "OTHERS", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(k.Key?.Replace("_", "").Replace(" ", ""), "OTHER", StringComparison.OrdinalIgnoreCase)))
            {
                rows.Add(new ReconciliationRowDto
                {
                    Description = kvp.Key,
                    Amount = kvp.Value
                });
            }
        }

        if (others > 0)
        {
            rows.Add(new ReconciliationRowDto
            {
                Description = "Others (Record)",
                Amount = others
            });
        }

        return rows;
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
