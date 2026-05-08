namespace FuelPro.Core.DTOs;

// ─────────────────────────────────────────────────────────────────────────────
//  AGS Import DTOs  (in-memory, not EF entities — used for preview/validation)
// ─────────────────────────────────────────────────────────────────────────────

public class AgsNozzleReadingDto
{
    public int NozzleNumber { get; set; }
    public string FuelType { get; set; } = string.Empty;   // "HSD" | "MS-I" | "MS-II"
    public int PumpNumber { get; set; }
    public double OpeningReading { get; set; }
    public double ClosingReading { get; set; }
    public double SaleLitres => ClosingReading - OpeningReading;
    public double TestingDeduction { get; set; }
    public double NetSaleLitres => SaleLitres - TestingDeduction;
}

public class AgsTankStockDto
{
    public int TankNumber { get; set; }
    public int AgsTankNumber { get; set; }
    public string FuelType { get; set; } = string.Empty;
    public double OpeningDipMM { get; set; }
    public double ClosingDipMM { get; set; }
    public double OpeningStockLitres { get; set; }
    public double ClosingStockLitres { get; set; }
    public double FuelDispensedLitres => OpeningStockLitres - ClosingStockLitres;
    public double ReceiptLitres { get; set; }
}

public class AgsShiftSummaryDto
{
    public double TotalHsdSaleLitres { get; set; }
    public double TotalMsISaleLitres { get; set; }
    public double TotalMsIISaleLitres { get; set; }
    public double GrandTotalSaleLitres => TotalHsdSaleLitres + TotalMsISaleLitres + TotalMsIISaleLitres;

    public double HsdOpeningStock { get; set; }
    public double HsdClosingStock { get; set; }
    public double MsIOpeningStock { get; set; }
    public double MsIClosingStock { get; set; }
    public double MsIIOpeningStock { get; set; }
    public double MsIIClosingStock { get; set; }

    public double VariationHsd { get; set; }
    public double VariationMsI { get; set; }
    public double VariationMsII { get; set; }
}

/// <summary>In-memory representation of a parsed AGS PDF (before persistence).</summary>
public class AgsShiftImportDto
{
    public DateTime ImportDate { get; set; }
    public string ShiftType { get; set; } = "A";
    public string PdfFileName { get; set; } = string.Empty;
    public string ImportedBy { get; set; } = string.Empty;
    public string? PdfPeriodFrom { get; set; }
    public string? PdfPeriodTo { get; set; }

    public List<AgsNozzleReadingDto> NozzleReadings { get; set; } = new();
    public List<AgsTankStockDto> TankStocks { get; set; } = new();
    public AgsShiftSummaryDto Summary { get; set; } = new();

    /// <summary>Diagnostic messages from the multi-strategy parser — e.g. which strategy fired.</summary>
    public List<string> ParseLog { get; set; } = new();
}

// ─────────────────────────────────────────────────────────────────────────────
//  Daily Summary DTO
// ─────────────────────────────────────────────────────────────────────────────

public class AgsDailySummaryDto
{
    public DateTime Date { get; set; }

    public double DayTotalHsdLitres { get; set; }
    public double DayTotalMsILitres { get; set; }
    public double DayTotalMsIILitres { get; set; }
    public double DayGrandTotalLitres => DayTotalHsdLitres + DayTotalMsILitres + DayTotalMsIILitres;

    public double HsdDayOpeningStock { get; set; }
    public double HsdDayClosingStock { get; set; }
    public double MsIDayOpeningStock { get; set; }
    public double MsIDayClosingStock { get; set; }
    public double MsIIDayOpeningStock { get; set; }
    public double MsIIDayClosingStock { get; set; }

    public bool ShiftAImported { get; set; }
    public bool ShiftBImported { get; set; }
    public bool ShiftCImported { get; set; }
    public bool AllShiftsImported => ShiftAImported && ShiftBImported && ShiftCImported;
    public int ShiftsImportedCount => (ShiftAImported ? 1 : 0) + (ShiftBImported ? 1 : 0) + (ShiftCImported ? 1 : 0);

    // Per-shift breakdown for the table widget
    public AgsShiftBreakdownDto? ShiftAData { get; set; }
    public AgsShiftBreakdownDto? ShiftBData { get; set; }
    public AgsShiftBreakdownDto? ShiftCData { get; set; }

    // Per-nozzle day totals
    public Dictionary<int, double> NozzleDaySales { get; set; } = new();
}

public class AgsShiftBreakdownDto
{
    public string ShiftType { get; set; } = string.Empty;
    public double HsdLitres { get; set; }
    public double MsILitres { get; set; }
    public double MsIILitres { get; set; }
    public double TotalLitres => HsdLitres + MsILitres + MsIILitres;
    public DateTime ImportedAt { get; set; }
    public string ImportedBy { get; set; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Import History DTO (for the import history DataGrid)
// ─────────────────────────────────────────────────────────────────────────────

public class AgsImportHistoryDto
{
    public int AgsShiftImportId { get; set; }
    public DateTime ImportDate { get; set; }
    public string ShiftType { get; set; } = string.Empty;
    public string ImportedBy { get; set; } = string.Empty;
    public string PdfFileName { get; set; } = string.Empty;
    public double TotalHsdLitres { get; set; }
    public double TotalMsILitres { get; set; }
    public double TotalMsIILitres { get; set; }
    public DateTime ImportedAt { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Validation
// ─────────────────────────────────────────────────────────────────────────────

public enum ImportWarningSeverity { Info, Warning, Error }

public class ImportWarning
{
    public ImportWarningSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;

    public ImportWarning(ImportWarningSeverity severity, string message)
    {
        Severity = severity;
        Message = message;
    }

    public string SeverityIcon => Severity switch
    {
        ImportWarningSeverity.Error   => "❌",
        ImportWarningSeverity.Warning => "⚠️",
        _                             => "ℹ️"
    };
}
