using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Models.AGS;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using Xunit;

namespace FuelPro.Tests;

public class ForensicAudit
{
    private readonly string _prodDbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FuelPro",
        "fuelPro.db"
    );

    [Fact]
    public async Task RunForensicAudit()
    {
        if (!File.Exists(_prodDbPath))
        {
            throw new FileNotFoundException("Production database not found", _prodDbPath);
        }

        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={_prodDbPath}")
        );

        using var provider = services.BuildServiceProvider();
        using var context = provider.GetRequiredService<FuelProDbContext>();

        // Load shifts chronologically (Shift B comes before Shift A on the same calendar date)
        var shifts = await context.Shifts
            .Include(s => s.DsmEntries)
                .ThenInclude(e => e.NozzleReadings)
            .ToListAsync();

        var sortedShifts = shifts
            .OrderBy(s => s.ShiftDate)
            .ThenBy(s => s.ShiftType == "A")
            .ToList();

        var report = new StringBuilder();
        report.AppendLine("# Forensic Audit Report: Tank & Nozzle Stock Persistence");
        report.AppendLine();
        report.AppendLine($"*Generated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");
        report.AppendLine($"*Database Path: `{_prodDbPath}`*");
        report.AppendLine();

        report.AppendLine("## Nozzle Readings Continuity Verification");
        report.AppendLine();
        report.AppendLine("Rule: `Current Shift Closing Reading == Next Shift Opening Reading` for every nozzle, shift, and day.");
        report.AppendLine();
        report.AppendLine("| Date | Shift | Pump | Nozzle | Fuel Type | Opening | Closing | Next Shift | Next Opening | Matches? |");
        report.AppendLine("|------|-------|------|--------|-----------|---------|---------|------------|--------------|----------|");

        int breakCount = 0;
        int checkCount = 0;

        for (int i = 0; i < sortedShifts.Count; i++)
        {
            var currentShift = sortedShifts[i];
            
            // Find next shift in chronological order
            Shift? nextShift = null;
            if (i + 1 < sortedShifts.Count)
            {
                nextShift = sortedShifts[i + 1];
            }

            foreach (var currentEntry in currentShift.DsmEntries)
            {
                foreach (var currentReading in currentEntry.NozzleReadings)
                {
                    checkCount++;
                    var nozzleNum = currentReading.NozzleNumber;
                    var closing = currentReading.ClosingReading;

                    // Look up this nozzle's opening reading in the next shift
                    double? nextOpening = null;
                    string nextShiftStr = "N/A";
                    
                    if (nextShift != null)
                    {
                        nextShiftStr = $"{nextShift.ShiftDate:yyyy-MM-dd} {nextShift.ShiftType}";
                        var nextEntry = nextShift.DsmEntries.FirstOrDefault(e => e.PumpId == currentEntry.PumpId || e.ConnectedPumpId == currentEntry.PumpId);
                        if (nextEntry != null)
                        {
                            var nextReading = nextEntry.NozzleReadings.FirstOrDefault(r => r.NozzleNumber == nozzleNum);
                            if (nextReading != null)
                            {
                                nextOpening = nextReading.OpeningReading;
                            }
                        }
                    }

                    var matches = nextOpening.HasValue && Math.Abs(closing - nextOpening.Value) < 0.001;
                    var matchesStr = matches ? "✅ Yes" : (nextOpening.HasValue ? "❌ NO" : "⚠️ No Next Shift Data");
                    if (!matches && nextOpening.HasValue)
                    {
                        breakCount++;
                    }

                    report.AppendLine($"| {currentShift.ShiftDate:yyyy-MM-dd} | {currentShift.ShiftType} | {currentEntry.PumpId} | {nozzleNum} | {currentReading.FuelType} | {currentReading.OpeningReading:F1} | {currentReading.ClosingReading:F1} | {nextShiftStr} | {(nextOpening.HasValue ? nextOpening.Value.ToString("F1") : "-")} | {matchesStr} |");
                }
            }
        }

        report.AppendLine();
        report.AppendLine($"**Summary:** Analyzed {checkCount} nozzle transitions. Found {breakCount} continuity breaks.");
        report.AppendLine();

        // Database persistence check
        report.AppendLine("## Nozzle Database Persistence Check");
        report.AppendLine();
        report.AppendLine("Verifying that nozzle readings are actually persisted in the database.");
        var totalNozzleRows = await context.NozzleReadings.CountAsync();
        report.AppendLine($"- Total saved nozzle reading rows in database: **{totalNozzleRows}**");
        report.AppendLine();

        // Tank stock persistence analysis
        report.AppendLine("## Tank Stock Persistence & Propagation Trace");
        report.AppendLine();
        report.AppendLine("Verifying how tank stocks evolve chronologically in `AgsShiftImports`:");
        report.AppendLine();
        report.AppendLine("| Import Date | Shift Type | HSD Opening | HSD Closing | MS-I Opening | MS-I Closing | MS-II Opening | MS-II Closing | Is Active |");
        report.AppendLine("|-------------|------------|-------------|-------------|--------------|--------------|---------------|---------------|-----------|");

        var imports = await context.AgsShiftImports
            .OrderBy(x => x.ImportDate)
            .ThenBy(x => x.ShiftType == "A")
            .ToListAsync();

        foreach (var imp in imports)
        {
            report.AppendLine($"| {imp.ImportDate:yyyy-MM-dd} | {imp.ShiftType} | {imp.HsdOpeningStock:F1} | {imp.HsdClosingStock:F1} | {imp.MsIOpeningStock:F1} | {imp.MsIClosingStock:F1} | {imp.MsIIOpeningStock:F1} | {imp.MsIIClosingStock:F1} | {imp.IsActive} |");
        }

        report.AppendLine();

        // Write the report to the artifacts directory
        string artifactDir = @"C:\Users\jadha\.gemini\antigravity-ide\brain\2486b7e1-2de2-40c8-8a51-ee499feae148";
        Directory.CreateDirectory(artifactDir);
        string reportPath = Path.Combine(artifactDir, "forensic_audit_report.md");
        await File.WriteAllTextAsync(reportPath, report.ToString());

        Console.WriteLine($"[AUDIT] Report written to: {reportPath}");
    }
}
