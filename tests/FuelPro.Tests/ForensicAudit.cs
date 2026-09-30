using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
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

    [Fact(Skip = "Diagnostic only")]
    public async Task InspectDatabaseNow()
    {
        if (!File.Exists(_prodDbPath)) return;

        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={_prodDbPath}")
        );

        using var provider = services.BuildServiceProvider();
        using var context = provider.GetRequiredService<FuelProDbContext>();

        var sb = new StringBuilder();
        sb.AppendLine("=== TANKS IN PROD DB ===");
        var tanks = await context.TankDefinitions.ToListAsync();
        foreach (var t in tanks)
        {
            sb.AppendLine($"Tank: Id={t.TankId}, Name='{t.TankName}', FuelType='{t.FuelType}', Active={t.IsActive}");
        }

        sb.AppendLine("\n=== PUMP MAPPINGS IN PROD DB ===");
        var mappings = await context.PumpMappings.ToListAsync();
        foreach (var m in mappings)
        {
            sb.AppendLine($"Mapping: Id={m.PumpMappingId}, Pump={m.PumpId}, Nozzle={m.NozzleNumber}, Fuel='{m.FuelType}', Tank='{m.TankName}', Active={m.IsActive}");
        }

        sb.AppendLine("\n=== SETTINGS ===");
        var settings = await context.Settings.FirstOrDefaultAsync();
        if (settings != null)
        {
            sb.AppendLine($"StationName='{settings.PumpStationName}'");
            sb.AppendLine($"TankDefinitionsJson: {settings.TankDefinitionsJson}");
            sb.AppendLine($"PumpMappingsJson: {settings.PumpMappingsJson}");
        }

        sb.AppendLine("\n=== RECENT SHIFTS & DSM ENTRIES ===");
        var shifts = await context.Shifts
            .Include(s => s.DsmEntries)
                .ThenInclude(e => e.NozzleReadings)
            .Include(s => s.DsmEntries)
                .ThenInclude(e => e.PaymentCollection)
                    .ThenInclude(p => p.Items)
            .OrderByDescending(s => s.ShiftDate)
            .Take(3)
            .ToListAsync();

        foreach (var s in shifts)
        {
            sb.AppendLine($"\nShift: Id={s.ShiftId}, Date={s.ShiftDate:yyyy-MM-dd}, Type={s.ShiftType}");
            foreach (var e in s.DsmEntries)
            {
                sb.AppendLine($"  DSM Entry: Id={e.DsmEntryId}, Name='{e.DsmName}', PumpId={e.PumpId}, ConnectedPumpId={e.ConnectedPumpId}, ReconciledToPumpId={e.ReconciledToPumpId}, GrossSales={e.GrossSales}, TotalCollection={e.TotalCollection}, Mismatch={e.Mismatch}");
                foreach (var nr in e.NozzleReadings)
                {
                    sb.AppendLine($"    Nozzle {nr.NozzleNumber}: {nr.OpeningReading} -> {nr.ClosingReading} = {nr.SaleLitres}L @ {nr.Rate} = Rs.{nr.Amount} (FuelType={nr.FuelType})");
                }
                var p = e.PaymentCollection;
                if (p != null)
                {
                    sb.AppendLine($"    PaymentCollection: Ph={p.PhonePe}, PhM={p.PhonePeMorning}, PhD={p.PhonePeDay}, PhN={p.PhonePeNight}, CC={p.CreditCard}, CCM={p.CreditCardMorning}, CCD={p.CreditCardDay}, CCN={p.CreditCardNight}, Petro={p.PetroCard}, PetroM={p.PetroCardMorning}, PetroD={p.PetroCardDay}, PetroN={p.PetroCardNight}");
                    if (p.Items != null)
                    {
                        foreach (var it in p.Items)
                        {
                            sb.AppendLine($"      Item: Code={it.CollectionTypeCode}, Amount={it.Amount}");
                        }
                    }
                }
                var denoms = await context.CashDenominations.Where(c => c.DsmEntryId == e.DsmEntryId).ToListAsync();
                foreach (var cd in denoms)
                {
                    sb.AppendLine($"    CashDenom: Type={cd.CashType}, Amount={cd.TotalAmount}");
                }
                var debits = await context.DebitEntries.Where(d => d.DsmEntryId == e.DsmEntryId).ToListAsync();
                foreach (var deb in debits)
                {
                    sb.AppendLine($"    Debit: Customer={deb.DebtorName}, Amount={deb.Amount}");
                }
                var expenses = await context.Expenses.Where(ex => ex.DsmEntryId == e.DsmEntryId).ToListAsync();
                foreach (var ex in expenses)
                {
                    sb.AppendLine($"    Expense: Desc={ex.Description}, Amount={ex.Amount}");
                }
                var testings = await context.TestingEntries.Where(t => t.DsmEntryId == e.DsmEntryId).ToListAsync();
                foreach (var t in testings)
                {
                    sb.AppendLine($"    Testing: Amount={t.Amount}");
                }
            }
        }

        var outPath = Path.Combine(Directory.GetCurrentDirectory(), "inspect_db_output.txt");
        await File.WriteAllTextAsync(outPath, sb.ToString());
    }

    [Fact(Skip = "Diagnostic only")]
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

        string artifactDir = Directory.GetCurrentDirectory();
        string reportPath = Path.Combine(artifactDir, "forensic_audit_report.md");
        await File.WriteAllTextAsync(reportPath, report.ToString());
        Console.WriteLine($"[AUDIT] Report written to: {reportPath}");
    }

    [Fact(Skip = "Diagnostic only")]
    public async Task DiagnosticAugust4()
    {
        if (!File.Exists(_prodDbPath)) return;

        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options => options.UseSqlite($"Data Source={_prodDbPath}"));
        using var provider = services.BuildServiceProvider();
        using var context = provider.GetRequiredService<FuelProDbContext>();

        var entries = await context.DsmEntries
            .Include(e => e.Shift)
            .Include(e => e.NozzleReadings)
            .Include(e => e.PaymentCollection)
            .Where(e => e.Shift != null && e.Shift.ShiftDate >= new DateTime(2026, 8, 1) && e.Shift.ShiftDate <= new DateTime(2026, 8, 10))
            .OrderBy(e => e.Shift!.ShiftDate)
            .ThenBy(e => e.Shift!.ShiftType)
            .ThenBy(e => e.PumpId)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine($"Entries around 04-Aug-2026: Count = {entries.Count}");
        foreach (var e in entries)
        {
            sb.AppendLine($"Entry ID {e.DsmEntryId}: Date={e.Shift?.ShiftDate:yyyy-MM-dd}, Shift={e.Shift?.ShiftType}, DSM='{e.DsmName}', Pump={e.PumpId}, ConnPump={e.ConnectedPumpId}, ReconTo={e.ReconciledToPumpId}, GrossSales={e.GrossSales}, Mismatch={e.Mismatch}");
            sb.AppendLine($"   NozzleReadings count: {e.NozzleReadings.Count}");
            foreach (var nr in e.NozzleReadings)
            {
                sb.AppendLine($"      Nozzle {nr.NozzleNumber}: Fuel={nr.FuelType}, Open={nr.OpeningReading}, Close={nr.ClosingReading}, Litres={nr.SaleLitres}, Rate={nr.Rate}, Amt={nr.Amount}");
            }
        }

        using var http = new HttpClient();
        string supUrl = "https://rvcibryprvjbzrtwqktk.supabase.co/rest/v1";
        string supKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ2Y2licnlwcnZqYnpydHdxa3RrIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODEzMTIxMTcsImV4cCI6MjA5Njg4ODExN30.vMTA97993upfnOCs5ja-kxIhDSHbcx1gEQ6itNm5BBk";
        http.DefaultRequestHeaders.Add("apikey", supKey);
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {supKey}");

        try
        {
            var pwaRes = await http.GetStringAsync($"{supUrl}/NozzleReadings?select=station_id,NozzleReadingId,NozzleNumber,ClosingReading,created_at&limit=20&order=NozzleReadingId.desc");
            sb.AppendLine("Distinct/Top station_ids in Supabase NozzleReadings:");
            sb.AppendLine(pwaRes);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Check station_ids FAILED: {ex.Message}");
        }

        // Test 1b: Query DsmApprovalAudits
        try
        {
            var audRes = await http.GetStringAsync($"{supUrl}/DsmApprovalAudits?select=DsmApprovalAuditId,SubmissionId,ApprovedAt,ApprovedDataJson,OriginalDataJson&order=ApprovedAt.desc&limit=5");
            sb.AppendLine("Supabase DsmApprovalAudits SUCCESS:");
            sb.AppendLine(audRes);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Supabase DsmApprovalAudits FAILED: {ex.Message}");
        }

        // Test 2: AgsShiftImports + AgsNozzleReadings query in Supabase
        try
        {
            var agsRes = await http.GetStringAsync($"{supUrl}/AgsShiftImports?select=SyncGuid,ShiftDate,ShiftType,AgsNozzleReadings(NozzleNumber,OpeningReading,ClosingReading,PumpNumber)&order=ShiftDate.desc&limit=5");
            sb.AppendLine("Supabase AgsShiftImports SUCCESS:");
            sb.AppendLine(agsRes);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Supabase AgsShiftImports FAILED: {ex.Message}");
        }

        var allAudits = await context.DsmApprovalAudits.ToListAsync();
        sb.AppendLine($"Total DsmApprovalAudits: {allAudits.Count}");
        foreach (var au in allAudits)
        {
            sb.AppendLine($"Audit {au.DsmApprovalAuditId}: SubId={au.SubmissionId}, Date={au.ApprovedAt}, Remarks={au.Remarks}");
            sb.AppendLine($"   ApprovedJson: {au.ApprovedDataJson}");
            sb.AppendLine($"   OrigJson: {au.OriginalDataJson}");
        }

        var nrsInRange = await context.NozzleReadings
            .Where(n => n.NozzleReadingId >= 900 && n.NozzleReadingId <= 1100)
            .OrderBy(n => n.NozzleReadingId)
            .ToListAsync();
        sb.AppendLine($"NozzleReadings with ID 900..1100: Count={nrsInRange.Count}");
        foreach (var nr in nrsInRange)
        {
            sb.AppendLine($"   NR {nr.NozzleReadingId}: DsmEntryId={nr.DsmEntryId}, Nozzle={nr.NozzleNumber}, Open={nr.OpeningReading}, Close={nr.ClosingReading}");
        }

        // Also check if there is an Audit for DsmEntryId 599 or Date 2026-08-04
        var audit599 = await context.DsmApprovalAudits
            .Where(a => a.ApprovedDataJson.Contains("599") || a.OriginalDataJson.Contains("599") || a.OriginalDataJson.Contains("2026-08-04"))
            .ToListAsync();
        sb.AppendLine($"Matching audits for 599/2026-08-04: {audit599.Count}");
        foreach (var au in audit599)
        {
            sb.AppendLine($"   Audit: SubId={au.SubmissionId}, Appr={au.ApprovedDataJson}");
            sb.AppendLine($"   Orig: {au.OriginalDataJson}");
        }

        // Check if there are other DsmEntries for 2026-08-04
        var dateEntries = await context.DsmEntries
            .Include(e => e.Shift)
            .Where(e => e.Shift != null && e.Shift.ShiftDate == new DateTime(2026, 8, 4))
            .ToListAsync();
        sb.AppendLine($"All entries for 2026-08-04: {dateEntries.Count}");
        foreach (var de in dateEntries)
        {
            sb.AppendLine($"   Entry {de.DsmEntryId}: Shift={de.Shift?.ShiftType}, DSM={de.DsmName}, Pump={de.PumpId}, GrossSales={de.GrossSales}");
        }

        var allNozzleReadings = await context.NozzleReadings.OrderByDescending(n => n.NozzleReadingId).Take(50).ToListAsync();
        sb.AppendLine($"Latest 50 NozzleReadings in DB:");
        foreach (var nr in allNozzleReadings)
        {
            sb.AppendLine($"   NR Id={nr.NozzleReadingId}, DsmEntryId={nr.DsmEntryId}, Nozzle={nr.NozzleNumber}, Open={nr.OpeningReading}, Close={nr.ClosingReading}");
        }

        string outPath = Path.Combine(Directory.GetCurrentDirectory(), "august4_diagnostic.txt");
        await File.WriteAllTextAsync(outPath, sb.ToString());
        Console.WriteLine($"WROTE DIAGNOSTIC TO {outPath}");
    }
}

