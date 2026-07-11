using FuelPro.Core.DTOs;
using FuelPro.Core.Services;
using Microsoft.EntityFrameworkCore;
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
}
