using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Serilog;

namespace FuelPro.Data.AppMigration;

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
                        // Others is NOT included in TotalInDirect — it is informational only
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

            // Find if there is a connected entry in the same shift for the same DSM that reconciles to this primary pump
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

                int primaryEntryId = 0;
                try
                {
                    var appObj = JsonConvert.DeserializeObject<dynamic>(audit.ApprovedDataJson);
                    primaryEntryId = (int)(appObj?.DsmEntryId ?? 0);
                }
                catch {}

                if (primaryEntryId <= 0) continue;

                var primaryEntry = await _dbContext.DsmEntries
                    .Include(e => e.NozzleReadings)
                    .FirstOrDefaultAsync(e => e.DsmEntryId == primaryEntryId);

                if (primaryEntry == null) continue;

                // Load slave entry linked to this primary entry
                DsmEntry? slaveEntry = null;
                if (primaryEntry.ConnectedPumpId.HasValue)
                {
                    slaveEntry = await _dbContext.DsmEntries
                        .Include(e => e.NozzleReadings)
                        .FirstOrDefaultAsync(e => e.ShiftId == primaryEntry.ShiftId
                            && e.PumpId == primaryEntry.ConnectedPumpId.Value
                            && (e.ReconciledToPumpId == primaryEntry.DsmEntryId || (e.ReconciledToPumpId == primaryEntry.PumpId && e.DsmName == primaryEntry.DsmName)));
                }

                // Parse original readings from audit
                var origObj = JsonConvert.DeserializeObject<dynamic>(audit.OriginalDataJson);
                var readingsToken = origObj?.Readings;
                if (readingsToken == null) continue;

                var expectedReadings = new List<(int NozzleId, double Opening, double Closing, double Rate)>();
                foreach (var r in readingsToken)
                {
                    int nId = (int)(r.NozzleId ?? r.nozzleId ?? 0);
                    double op = (double)(r.OpeningReading ?? r.openingReading ?? 0);
                    double cl = (double)(r.ClosingReading ?? r.closingReading ?? 0);
                    double rt = (double)(r.Rate ?? r.rate ?? 0);
                    if (nId > 0) expectedReadings.Add((nId, op, cl, rt));
                }

                if (expectedReadings.Count == 0) continue;

                bool entryHealed = false;

                // Check and fix slave entry readings
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
                                entryHealed = true;
                            }
                        }
                    }

                    if (entryHealed)
                    {
                        slaveEntry.GrossSales = (decimal)slaveEntry.NozzleReadings.Sum(r => r.Amount);
                        slaveEntry.Mismatch = 0m;
                        _dbContext.Entry(slaveEntry).State = EntityState.Modified;
                    }
                }

                // Check and fix primary entry readings
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
                                entryHealed = true;
                            }
                        }
                    }
                }

                if (entryHealed)
                {
                    double primaryGross = primaryEntry.NozzleReadings?.Sum(r => r.Amount) ?? (double)primaryEntry.GrossSales;
                    double slaveGross = slaveEntry?.NozzleReadings?.Sum(r => r.Amount) ?? (double)(slaveEntry?.GrossSales ?? 0m);
                    primaryEntry.GrossSales = (decimal)(primaryGross + slaveGross);
                    primaryEntry.Mismatch = primaryEntry.TotalCollection - primaryEntry.GrossSales;
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
