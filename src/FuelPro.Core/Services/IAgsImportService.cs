using FuelPro.Core.DTOs;

namespace FuelPro.Core.Services;

public interface IAgsImportService
{
    /// <summary>
    /// Parses an AGS Field Officer PDF from the given file path.
    /// Returns a populated AgsShiftImportDto for preview and validation — does NOT persist anything.
    /// ParseLog on the DTO records which extraction strategy fired.
    /// </summary>
    Task<AgsShiftImportDto> ParseAgsReportAsync(string pdfFilePath, DateTime date, string shiftType);

    /// <summary>
    /// Dumps all words with X/Y coordinates and row-grouped text from a PDF to a UTF-8 file.
    /// Call this temporarily to inspect raw PdfPig output and tune regex patterns.
    /// Returns the dump file path.
    /// </summary>
    Task<string> DumpPdfTextAsync(string pdfFilePath, string outputDir);
}

