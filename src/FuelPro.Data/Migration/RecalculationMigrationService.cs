using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Serilog;

namespace FuelPro.Data.AppMigration;

public class AuditOriginalDataDto
{
    public List<AuditNozzleReadingDto>? Readings { get; set; }
    public List<AuditNozzleReadingDto>? nozzleReadings { get; set; }
}

public class AuditNozzleReadingDto
{
    public int NozzleId { get; set; }
    public int nozzleId { get; set; }
    public double OpeningReading { get; set; }
    public double openingReading { get; set; }
    public double ClosingReading { get; set; }
    public double closingReading { get; set; }
    public double Rate { get; set; }
    public double rate { get; set; }

    public int GetNozzleId() => NozzleId > 0 ? NozzleId : nozzleId;
    public double GetOpening() => OpeningReading > 0 ? OpeningReading : openingReading;
    public double GetClosing() => ClosingReading > 0 ? ClosingReading : closingReading;
    public double GetRate() => Rate > 0 ? Rate : rate;
}

public class AuditApprovedDataDto
{
    public int DsmEntryId { get; set; }
    public int dsmEntryId { get; set; }
    public int GetDsmEntryId() => DsmEntryId > 0 ? DsmEntryId : dsmEntryId;
}

public class RecalculationMigrationService
{
    private readonly FuelProDbContext _dbContext;
    private readonly IDsmCalculationService _dsmCalculationService;
    private readonly ILogger _logger = Log.ForContext<RecalculationMigrationService>();

    public RecalculationMigrationService(FuelProDbContext dbContext, IDsmCalculationService dsmCalculationService)
    {
        _dbContext = dbContext;
        _dsmCalculationService = dsmCalculationService;
    }

    public async Task RunIfNeededAsync()
    {
        await HealCorruptedNozzleReadingsAsync();

        var done = await _dbContext.AppMeta.FirstOrDefaultAsync(x => x.Key == "RecalcMigrationV2Done");
        if (done?.Value == "true") return;

        var entries = await _dbContext.DsmEntries
            .Include(e => e.NozzleReadings)
            .Include(e => e.PaymentCollection)
            .Include(e => e.DebitEntries)
            .Include(e => e.TestingEntries)
            .Include(e => e.Expenses)
            .Include(e => e.CashDenominations)
            .ToListAsync();

        var updatedCount = 0;
        foreach (var entry in entries)
        {
            try
            {
                var calc = _dsmCalculationService.Calculate(new DsmEntryDto
                {
                    DSMEntryId = entry.DsmEntryId,
                    NozzleReadings = entry.NozzleReadings.Select(n => new NozzleReadingDto { Amount = (decimal)n.Amount }).ToList(),
                    PaymentCollection = new PaymentCollectionDto
                    {
                        PhonePe      = (decimal)((entry.PaymentCollection?.PhonePe ?? 0)
                                                + (entry.PaymentCollection?.PhonePeCard ?? 0)),
                        CreditCard   = (decimal)((entry.PaymentCollection?.CreditCard ?? 0) + (entry.PaymentCollection?.PetroCard ?? 0)),
                        CashDeposit  = (decimal)(entry.PaymentCollection?.CashDeposit ?? 0),
                        PhysicalCash = (decimal)(entry.CashDenominations.Where(x => x.CashType == "Cash1").Sum(x => x.TotalAmount)
                                                + entry.CashDenominations.Where(x => x.CashType == "Cash2").Sum(x => x.TotalAmount))
                    },
                    DebitEntries   = entry.DebitEntries.Select(d => new DebitEntryDto { Amount = (decimal)d.Amount }).ToList(),
                    Expenses       = entry.Expenses.Select(e => new ExpenseDto { Amount = (decimal)e.Amount }).ToList(),
                    TestingEntries = entry.TestingEntries.Select(t => new TestingEntryDto { Amount = (decimal)t.Amount }).ToList()
                });

                entry.GrossSales = calc.GrossSales;
                entry.TotalInDirect = calc.TotalInDirect;
                entry.TotalCreditors = calc.TotalCreditors;
                entry.TotalCollection = calc.TotalCollection;
                entry.Mismatch = calc.Mismatch;
                updatedCount++;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed recalculation for DSMEntryId: {Id}", entry.DsmEntryId);
            }
        }

        await _dbContext.SaveChangesAsync();
        _dbContext.AppMeta.Add(new Core.Models.AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });
        await _dbContext.SaveChangesAsync();
        _logger.Information("Recalculation migration completed for {Count} entries.", updatedCount);

        await RunConnectedPumpLinkRepairAsync();
    }

    private async Task RunConnectedPumpLinkRepairAsync()
    {
        var done = await _dbContext.AppMeta.FirstOrDefaultAsync(x => x.Key == "ConnectedPumpLinkRepairDone");
        if (done?.Value == "true") return;

        var entries = await _dbContext.DsmEntries.ToListAsync();
        var primaryEntries = entries.Where(e => !e.ReconciledToPumpId.HasValue).ToList();
        var connectedEntries = entries.Where(e => e.ReconciledToPumpId.HasValue).ToList();

        var updatedCount = 0;
        foreach (var primary in primaryEntries)
        {
            if (primary.ConnectedPumpId.HasValue) continue;

            var match = connectedEntries.FirstOrDefault(c =>
                c.ShiftId == primary.ShiftId
                && c.ReconciledToPumpId == primary.PumpId
                && string.Equals(c.DsmName, primary.DsmName, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                primary.ConnectedPumpId = match.PumpId;
                _dbContext.Entry(primary).State = EntityState.Modified;
                updatedCount++;
            }
        }

        if (updatedCount > 0)
        {
            await _dbContext.SaveChangesAsync();
        }

        _dbContext.AppMeta.Add(new Core.Models.AppMeta { Key = "ConnectedPumpLinkRepairDone", Value = "true" });
        await _dbContext.SaveChangesAsync();
        _logger.Information("Connected pump link repair migration completed. Linked {Count} primary entries.", updatedCount);
    }

    private async Task HealCorruptedNozzleReadingsAsync()
    {
        try
        {
            var audits = await _dbContext.DsmApprovalAudits.ToListAsync();
            if (!audits.Any()) return;

            bool anyRepaired = false;
            foreach (var audit in audits)
            {
                if (string.IsNullOrWhiteSpace(audit.OriginalDataJson) || string.IsNullOrWhiteSpace(audit.ApprovedDataJson))
                    continue;

                int approvedEntryId = 0;
                try
                {
                    var appObj = JsonConvert.DeserializeObject<AuditApprovedDataDto>(audit.ApprovedDataJson);
                    approvedEntryId = appObj?.GetDsmEntryId() ?? 0;
                }
                catch {}

                if (approvedEntryId <= 0) continue;

                AuditOriginalDataDto? origObj = null;
                try
                {
                    origObj = JsonConvert.DeserializeObject<AuditOriginalDataDto>(audit.OriginalDataJson);
                }
                catch {}

                var rawReadings = origObj?.Readings ?? origObj?.nozzleReadings;
                if (rawReadings == null || !rawReadings.Any()) continue;

                var expectedReadings = rawReadings
                    .Select(r => (NozzleId: r.GetNozzleId(), Opening: r.GetOpening(), Closing: r.GetClosing(), Rate: r.GetRate()))
                    .Where(x => x.NozzleId > 0)
                    .ToList();

                if (!expectedReadings.Any()) continue;

                // Find primary entry matching this audit by entry ID or matching DSM name and shift
                var entry = await _dbContext.DsmEntries
                    .Include(e => e.NozzleReadings)
                    .Include(e => e.TestingEntries)
                    .Include(e => e.Shift)
                    .FirstOrDefaultAsync(e => e.DsmEntryId == approvedEntryId);

                if (entry == null) continue;

                DsmEntry primaryEntry = entry;
                // If approvedEntryId points to a slave entry, find its primary entry
                if (entry.ReconciledToPumpId.HasValue)
                {
                    var realPrimary = await _dbContext.DsmEntries
                        .Include(e => e.NozzleReadings)
                        .Include(e => e.TestingEntries)
                        .Include(e => e.Shift)
                        .FirstOrDefaultAsync(e => e.DsmEntryId == entry.ReconciledToPumpId.Value);

                    if (realPrimary == null)
                    {
                        // Convert entry to primary
                        entry.ReconciledToPumpId = null;
                        entry.PumpId = 1;
                        entry.ConnectedPumpId = 2;
                        realPrimary = entry;
                    }
                    primaryEntry = realPrimary;
                }

                if (!primaryEntry.ConnectedPumpId.HasValue)
                {
                    primaryEntry.ConnectedPumpId = 2;
                }

                DateTime? shiftDate = primaryEntry.Shift?.ShiftDate ?? (primaryEntry.CreatedAt != default ? primaryEntry.CreatedAt.Date : (DateTime?)null);

                // Look for slave entry linked to primaryEntry.DsmEntryId
                DsmEntry? slaveEntry = await _dbContext.DsmEntries
                    .Include(e => e.NozzleReadings)
                    .Include(e => e.TestingEntries)
                    .FirstOrDefaultAsync(e => e.ShiftId == primaryEntry.ShiftId
                        && e.PumpId == primaryEntry.ConnectedPumpId.Value
                        && e.ReconciledToPumpId == primaryEntry.DsmEntryId);

                bool nozzleReadingsHealed = false;
                bool slaveEntryCreated = false;
                bool slaveLinkRepaired = false;

                // If slave entry missing or incorrectly linked, repair link or create it
                if (slaveEntry == null)
                {
                    var connNozzles = PumpConfiguration.GetNozzlesForPump(primaryEntry.ConnectedPumpId.Value, shiftDate);
                    var targetNozzleIds = connNozzles != null && connNozzles.Length > 0 ? connNozzles : (primaryEntry.ConnectedPumpId.Value == 2 ? new int[] { 2, 4 } : Array.Empty<int>());

                    var connReadings = expectedReadings.Where(r => targetNozzleIds.Contains(r.NozzleId)).ToList();
                    if (connReadings.Any())
                    {
                        // Check if there is an unlinked/mismatched slave entry we can re-assign
                        slaveEntry = await _dbContext.DsmEntries
                            .Include(e => e.NozzleReadings)
                            .Include(e => e.TestingEntries)
                            .FirstOrDefaultAsync(e => e.ShiftId == primaryEntry.ShiftId
                                && e.PumpId == primaryEntry.ConnectedPumpId.Value
                                && e.ReconciledToPumpId.HasValue
                                && e.DsmName.ToLower() == primaryEntry.DsmName.ToLower());

                        if (slaveEntry != null)
                        {
                            slaveEntry.ReconciledToPumpId = primaryEntry.DsmEntryId;
                            _dbContext.Entry(slaveEntry).State = EntityState.Modified;
                            slaveLinkRepaired = true;
                            anyRepaired = true;
                        }
                        else
                        {
                            slaveEntry = new DsmEntry
                            {
                                ShiftId = primaryEntry.ShiftId,
                                PumpId = primaryEntry.ConnectedPumpId.Value,
                                ReconciledToPumpId = primaryEntry.DsmEntryId,
                                DsmName = primaryEntry.DsmName,
                                GrossSales = 0m,
                                TotalCollection = 0m,
                                Mismatch = 0m,
                                CreatedAt = primaryEntry.CreatedAt,
                                UpdatedAt = DateTime.Now
                            };
                            _dbContext.DsmEntries.Add(slaveEntry);
                            await _dbContext.SaveChangesAsync();
                            slaveEntryCreated = true;
                            anyRepaired = true;
                        }

                        if (slaveLinkRepaired)
                        {
                            _logger.Information("Re-linked existing slave entry {SlaveId} to primary entry {PrimaryId} (link-only repair)", slaveEntry.DsmEntryId, primaryEntry.DsmEntryId);
                        }

                        if (slaveEntry.NozzleReadings == null) slaveEntry.NozzleReadings = new List<NozzleReading>();

                        foreach (var cr in connReadings)
                        {
                            var existingNr = slaveEntry.NozzleReadings.FirstOrDefault(n => n.NozzleNumber == cr.NozzleId);
                            if (existingNr != null)
                            {
                                if (Math.Abs(existingNr.OpeningReading - cr.Opening) > 0.001 || Math.Abs(existingNr.ClosingReading - cr.Closing) > 0.001)
                                {
                                    existingNr.OpeningReading = cr.Opening;
                                    existingNr.ClosingReading = cr.Closing;
                                    existingNr.SaleLitres = cr.Closing - cr.Opening;
                                    existingNr.Rate = cr.Rate;
                                    existingNr.Amount = (cr.Closing - cr.Opening) * cr.Rate;
                                    nozzleReadingsHealed = true;
                                    anyRepaired = true;
                                }
                            }
                            else
                            {
                                var nr = new NozzleReading
                                {
                                    DsmEntryId = slaveEntry.DsmEntryId,
                                    NozzleNumber = cr.NozzleId,
                                    OpeningReading = cr.Opening,
                                    ClosingReading = cr.Closing,
                                    SaleLitres = cr.Closing - cr.Opening,
                                    Rate = cr.Rate,
                                    Amount = (cr.Closing - cr.Opening) * cr.Rate
                                };
                                slaveEntry.NozzleReadings.Add(nr);
                                if (!slaveEntryCreated)
                                {
                                    nozzleReadingsHealed = true;
                                }
                                anyRepaired = true;
                            }
                        }
                        slaveEntry.GrossSales = (decimal)slaveEntry.NozzleReadings.Sum(r => r.Amount);
                        await _dbContext.SaveChangesAsync();
                    }
                }

                bool slaveHealed = false;

                // Fix slave entry readings if slave exists
                if (slaveEntry != null && slaveEntry.NozzleReadings != null)
                {
                    foreach (var sReading in slaveEntry.NozzleReadings)
                    {
                        var exp = expectedReadings.FirstOrDefault(x => x.NozzleId == sReading.NozzleNumber);
                        if (exp.NozzleId > 0)
                        {
                            if (Math.Abs(sReading.OpeningReading - exp.Opening) > 0.001 || Math.Abs(sReading.ClosingReading - exp.Closing) > 0.001)
                            {
                                sReading.OpeningReading = exp.Opening;
                                sReading.ClosingReading = exp.Closing;
                                sReading.SaleLitres = sReading.ClosingReading - sReading.OpeningReading;
                                sReading.Amount = sReading.SaleLitres * sReading.Rate;
                                slaveHealed = true;
                                nozzleReadingsHealed = true;
                                anyRepaired = true;
                            }
                        }
                    }
                }

                // Fix primary entry readings
                if (primaryEntry.NozzleReadings != null)
                {
                    foreach (var pReading in primaryEntry.NozzleReadings)
                    {
                        var exp = expectedReadings.FirstOrDefault(x => x.NozzleId == pReading.NozzleNumber);
                        if (exp.NozzleId > 0)
                        {
                            if (Math.Abs(pReading.OpeningReading - exp.Opening) > 0.001 || Math.Abs(pReading.ClosingReading - exp.Closing) > 0.001)
                            {
                                pReading.OpeningReading = exp.Opening;
                                pReading.ClosingReading = exp.Closing;
                                pReading.SaleLitres = pReading.ClosingReading - pReading.OpeningReading;
                                pReading.Amount = pReading.SaleLitres * pReading.Rate;
                                nozzleReadingsHealed = true;
                                anyRepaired = true;
                            }
                        }
                    }
                }

                // --- STRICT AUTHORITATIVE NOZZLE COMPLETENESS VALIDATION ---
                int[] configuredPrimaryNozzles = shiftDate.HasValue 
                    ? (PumpConfiguration.GetNozzlesForPump(primaryEntry.PumpId, shiftDate.Value) ?? Array.Empty<int>())
                    : Array.Empty<int>();

                int[] configuredSlaveNozzles = (shiftDate.HasValue && primaryEntry.ConnectedPumpId.HasValue)
                    ? (PumpConfiguration.GetNozzlesForPump(primaryEntry.ConnectedPumpId.Value, shiftDate.Value) ?? Array.Empty<int>())
                    : Array.Empty<int>();

                var auditNozzleIds = expectedReadings.Select(r => r.NozzleId).ToList();
                bool auditHasDuplicates = auditNozzleIds.Count != auditNozzleIds.Distinct().Count();

                var auditPrimaryIds = auditNozzleIds.Where(id => configuredPrimaryNozzles.Contains(id)).Distinct().ToList();
                bool auditPrimaryExact = !auditHasDuplicates 
                    && configuredPrimaryNozzles.Length > 0 
                    && auditPrimaryIds.Count == configuredPrimaryNozzles.Length 
                    && configuredPrimaryNozzles.All(id => auditPrimaryIds.Contains(id));

                var auditSlaveIds = auditNozzleIds.Where(id => configuredSlaveNozzles.Contains(id)).Distinct().ToList();
                bool auditSlaveExact = !auditHasDuplicates 
                    && (!primaryEntry.ConnectedPumpId.HasValue || (configuredSlaveNozzles.Length > 0 && auditSlaveIds.Count == configuredSlaveNozzles.Length && configuredSlaveNozzles.All(id => auditSlaveIds.Contains(id))));

                var allConfiguredNozzles = configuredPrimaryNozzles.Concat(configuredSlaveNozzles).ToHashSet();
                bool auditHasUnexpected = auditNozzleIds.Any(id => !allConfiguredNozzles.Contains(id));

                var actualPrimaryNozzles = primaryEntry.NozzleReadings?.ToList() ?? new List<NozzleReading>();
                bool primaryHasDuplicates = actualPrimaryNozzles.GroupBy(n => n.NozzleNumber).Any(g => g.Count() > 1);
                bool primaryHasUnexpected = actualPrimaryNozzles.Any(n => !configuredPrimaryNozzles.Contains(n.NozzleNumber));
                bool primaryMissingAny = configuredPrimaryNozzles.Any(id => !actualPrimaryNozzles.Any(n => n.NozzleNumber == id));

                bool primaryValidAndComplete = !primaryHasDuplicates && !primaryHasUnexpected && !primaryMissingAny
                    && configuredPrimaryNozzles.Length > 0 && actualPrimaryNozzles.Count == configuredPrimaryNozzles.Length;

                bool slaveValidAndComplete = true;
                List<NozzleReading> actualSlaveNozzles = new();
                if (primaryEntry.ConnectedPumpId.HasValue)
                {
                    actualSlaveNozzles = slaveEntry?.NozzleReadings?.ToList() ?? new List<NozzleReading>();
                    bool slaveHasDuplicates = actualSlaveNozzles.GroupBy(n => n.NozzleNumber).Any(g => g.Count() > 1);
                    bool slaveHasUnexpected = actualSlaveNozzles.Any(n => !configuredSlaveNozzles.Contains(n.NozzleNumber));
                    bool slaveMissingAny = configuredSlaveNozzles.Any(id => !actualSlaveNozzles.Any(n => n.NozzleNumber == id));

                    slaveValidAndComplete = slaveEntry != null && !slaveHasDuplicates && !slaveHasUnexpected && !slaveMissingAny
                        && configuredSlaveNozzles.Length > 0 && actualSlaveNozzles.Count == configuredSlaveNozzles.Length;
                }

                bool hasCompleteNozzleData = shiftDate.HasValue
                    && configuredPrimaryNozzles.Length > 0
                    && (!primaryEntry.ConnectedPumpId.HasValue || (configuredSlaveNozzles.Length > 0 && !configuredPrimaryNozzles.Intersect(configuredSlaveNozzles).Any()))
                    && !auditHasDuplicates
                    && auditPrimaryExact
                    && auditSlaveExact
                    && !auditHasUnexpected
                    && primaryValidAndComplete
                    && slaveValidAndComplete;

                decimal primaryGross = hasCompleteNozzleData ? (decimal)actualPrimaryNozzles.Sum(n => n.Amount) : 0m;
                decimal slaveGross = (hasCompleteNozzleData && primaryEntry.ConnectedPumpId.HasValue) ? (decimal)actualSlaveNozzles.Sum(n => n.Amount) : 0m;
                decimal combinedNozzleGross = primaryGross + slaveGross;

                bool grossDiscrepancy = hasCompleteNozzleData && Math.Abs(primaryEntry.GrossSales - combinedNozzleGross) > 0.01m;

                if (!hasCompleteNozzleData)
                {
                    _logger.Warning("Incomplete nozzle dataset for approved entry {EntryId} (ShiftDate: {ShiftDate}, Pump: {PumpId}, ConnectedPump: {ConnectedPumpId}). Skipping financial recalculation.",
                        primaryEntry.DsmEntryId, shiftDate, primaryEntry.PumpId, primaryEntry.ConnectedPumpId);
                }

                if (hasCompleteNozzleData && (nozzleReadingsHealed || slaveEntryCreated || grossDiscrepancy))
                {
                    if (slaveEntry != null && (slaveHealed || slaveEntryCreated))
                    {
                        slaveEntry.GrossSales = slaveGross;
                        slaveEntry.Mismatch = 0m;
                        _dbContext.Entry(slaveEntry).State = EntityState.Modified;
                    }

                    primaryEntry.GrossSales = combinedNozzleGross;

                    // Aggregate testing entries across primary and slave exactly once
                    var groupTestingList = new List<TestingEntry>();
                    if (primaryEntry.TestingEntries != null)
                    {
                        groupTestingList.AddRange(primaryEntry.TestingEntries);
                    }
                    if (slaveEntry?.TestingEntries != null)
                    {
                        foreach (var st in slaveEntry.TestingEntries)
                        {
                            if (st.TestingId > 0 && groupTestingList.Any(t => t.TestingId == st.TestingId))
                                continue;
                            if (groupTestingList.Contains(st))
                                continue;
                            groupTestingList.Add(st);
                        }
                    }

                    decimal groupTesting = (decimal)groupTestingList.Sum(t => t.Amount);
                    decimal netSales = primaryEntry.GrossSales - groupTesting;
                    primaryEntry.Mismatch = primaryEntry.TotalCollection - netSales;
                    _dbContext.Entry(primaryEntry).State = EntityState.Modified;
                    anyRepaired = true;
                }
            }

            if (anyRepaired)
            {
                await _dbContext.SaveChangesAsync();
                _logger.Information("Successfully auto-healed corrupted nozzle readings for approved submissions.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to auto-heal corrupted approved entries.");
        }
    }
}
