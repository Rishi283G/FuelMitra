using FuelPro.Core.DTOs;
using Serilog;

namespace FuelPro.Core.Services;

/// <summary>
/// Validates a parsed AGS shift import DTO before it is persisted.
/// Returns a list of ImportWarning objects — errors block save, warnings are advisory.
/// </summary>
public class AgsImportValidator
{
    private static readonly ILogger _logger = Log.ForContext<AgsImportValidator>();

    // Max permissible variance between nozzle total and tank dispensed (%)
    public const double MaxVariancePct = 0.5;

    // Max realistic sale per nozzle per shift (L) — flag for review if exceeded
    public const double MaxNozzleSalePerShift = 2000.0;

    // Shift boundary definitions (24h clock hours)
    private static readonly Dictionary<string, (int FromHour, int ToHour)> ShiftBoundaries = new()
    {
        { "A", (6,  14) },
        { "B", (14, 22) },
        { "C", (22, 30) },  // 30 = 06:00 next day
    };

    public List<ImportWarning> Validate(AgsShiftImportDto import, bool shiftAlreadyExists)
    {
        var warnings = new List<ImportWarning>();

        // Rule 1: Duplicate shift check
        if (shiftAlreadyExists)
            warnings.Add(new ImportWarning(ImportWarningSeverity.Warning,
                $"Shift {import.ShiftType} for {import.ImportDate:dd-MMM-yyyy} is already imported. Saving will replace the previous import."));

        // Rule 2: No nozzle readings extracted
        if (!import.NozzleReadings.Any())
            warnings.Add(new ImportWarning(ImportWarningSeverity.Error,
                "No nozzle readings could be extracted from the PDF. Check that the correct AGS report is selected."));

        // Rule 3: No tank readings extracted  
        if (!import.TankStocks.Any())
            warnings.Add(new ImportWarning(ImportWarningSeverity.Warning,
                "No tank stock readings were extracted. Tank dashboard widgets will show zero values."));

        // Rule 4: Negative nozzle sales
        foreach (var n in import.NozzleReadings.Where(n => n.SaleLitres < 0))
            warnings.Add(new ImportWarning(ImportWarningSeverity.Error,
                $"Nozzle {n.NozzleNumber} ({n.FuelType}): Negative sale {n.SaleLitres:F2} L — closing reading is less than opening."));

        // Rule 5: Zero readings (flag, don't error)
        var zeroNozzles = import.NozzleReadings.Where(n => n.SaleLitres == 0).ToList();
        if (zeroNozzles.Any())
            warnings.Add(new ImportWarning(ImportWarningSeverity.Info,
                $"{zeroNozzles.Count} nozzle(s) show zero sale (may be offline): {string.Join(", ", zeroNozzles.Select(n => $"N{n.NozzleNumber}"))}"));

        // Rule 6: Unrealistic nozzle jumps
        foreach (var n in import.NozzleReadings.Where(n => n.SaleLitres > MaxNozzleSalePerShift))
            warnings.Add(new ImportWarning(ImportWarningSeverity.Warning,
                $"Nozzle {n.NozzleNumber} ({n.FuelType}): High sale {n.SaleLitres:F0} L in this shift — please verify."));

        // Rule 8: Shift boundary check (if period timestamps extracted from PDF)
        if (!string.IsNullOrEmpty(import.PdfPeriodFrom) && ShiftBoundaries.TryGetValue(import.ShiftType, out var bounds))
        {
            if (TryParseHour(import.PdfPeriodFrom, out int fromHour))
            {
                bool matchesShift = import.ShiftType switch
                {
                    "A" => fromHour == bounds.FromHour,
                    "B" => fromHour == bounds.FromHour,
                    "C" => fromHour == 22 || fromHour == 0,
                    _   => true
                };

                if (!matchesShift)
                    warnings.Add(new ImportWarning(ImportWarningSeverity.Warning,
                        $"PDF period starts at {import.PdfPeriodFrom}, which may not match Shift {import.ShiftType} (expected ~{bounds.FromHour:00}:00). Please verify the correct shift is selected."));
            }
        }

        // Rule 9: Tank stock continuity warning (closing < opening suggests receipt not recorded)
        foreach (var tank in import.TankStocks.Where(t => t.FuelDispensedLitres < 0))
            warnings.Add(new ImportWarning(ImportWarningSeverity.Warning,
                $"Tank {tank.TankNumber} ({tank.FuelType}): Closing stock ({tank.ClosingStockLitres:F0} L) > Opening stock ({tank.OpeningStockLitres:F0} L). A receipt may have occurred during this shift."));

        _logger.Information("AGS Validation: {Errors} error(s), {Warnings} warning(s)",
            warnings.Count(w => w.Severity == ImportWarningSeverity.Error),
            warnings.Count(w => w.Severity == ImportWarningSeverity.Warning));

        return warnings;
    }

    private static bool TryParseHour(string timeStr, out int hour)
    {
        hour = 0;
        var m = System.Text.RegularExpressions.Regex.Match(timeStr, @"(\d{1,2})[:/]");
        if (m.Success && int.TryParse(m.Groups[1].Value, out hour))
            return true;
        return false;
    }

    public bool HasBlockingErrors(List<ImportWarning> warnings)
        => warnings.Any(w => w.Severity == ImportWarningSeverity.Error);
}
