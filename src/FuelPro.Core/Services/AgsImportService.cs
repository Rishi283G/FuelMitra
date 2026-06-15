using System.Text;
using System.Text.RegularExpressions;
using FuelPro.Core.DTOs;
using Newtonsoft.Json;
using Serilog;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace FuelPro.Core.Services;

/// <summary>
/// Parses AGS (Automatic Gauge System) Field Officer PDF reports.
/// Uses PdfPig for text extraction with coordinate-based row grouping.
/// Falls back to regex line scanning for robustness.
/// 
/// NOTE: AGS PDF layouts vary by manufacturer/firmware. This parser is tuned for
/// the Tokheim/Wayne/Gilbarco ATG report format typically used at Indian petrol pumps.
/// Raw extracted text is logged at Debug level for inspection/tuning.
/// </summary>
public class AgsImportService : IAgsImportService
{
    private readonly ILogger _logger = Log.ForContext<AgsImportService>();

    // ─────────────────────────────────────────────────────────────────────────
    //  Hardcoded AGS key mapping for Site 215436
    //  Maps: DU|Pump|Nozzle (physical machine numbers) -> Operational Pump (1-8)
    // ─────────────────────────────────────────────────────────────────────────
    private sealed record AgsNozzleMeta(int PhysicalNozzle, int PumpNumber, string FuelType);

    private static readonly Dictionary<string, AgsNozzleMeta> AgsNozzleMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Operational Pump 1 (Machine 1 Side A)
        { "1|1|1", new AgsNozzleMeta(1, 1, "MS-I") },
        { "1|1|2", new AgsNozzleMeta(2, 1, "MS-II") },

        // Operational Pump 2 (Machine 1 Side B)
        { "1|1|3", new AgsNozzleMeta(3, 2, "MS-I") },
        { "1|1|4", new AgsNozzleMeta(4, 2, "MS-II") },

        // Operational Pump 3 (Machine 2 Side A)
        { "1|2|1", new AgsNozzleMeta(5, 3, "MS-I") },
        { "1|2|2", new AgsNozzleMeta(6, 3, "MS-II") },
        { "1|2|3", new AgsNozzleMeta(7, 3, "HSD") },

        // Operational Pump 4 (Machine 2 Side B)
        { "1|2|4", new AgsNozzleMeta(8, 4, "MS-I") },
        { "1|2|5", new AgsNozzleMeta(9, 4, "MS-II") },
        { "1|2|6", new AgsNozzleMeta(10, 4, "HSD") },

        // Operational Pump 5 (Machine 3 Side A)
        { "2|3|1", new AgsNozzleMeta(11, 5, "MS-I") },
        { "2|3|2", new AgsNozzleMeta(12, 5, "MS-II") },
        { "2|3|3", new AgsNozzleMeta(13, 5, "HSD") },

        // Operational Pump 6 (Machine 3 Side B)
        { "2|3|4", new AgsNozzleMeta(14, 6, "MS-I") },
        { "2|3|5", new AgsNozzleMeta(15, 6, "MS-II") },
        { "2|3|6", new AgsNozzleMeta(16, 6, "HSD") },

        // Operational Pump 7 (Machine 4 Side A)
        { "2|4|1", new AgsNozzleMeta(17, 7, "HSD") },
        { "2|4|2", new AgsNozzleMeta(18, 7, "HSD") },

        // Operational Pump 8 (Machine 4 Side B)
        { "2|4|3", new AgsNozzleMeta(19, 8, "HSD") },
        { "2|4|4", new AgsNozzleMeta(20, 8, "HSD") }
    };

    private sealed record AgsTankMeta(int InternalTankId, string FuelType);

    private static readonly Dictionary<int, AgsTankMeta> AgsTankMap = new()
    {
        { 1, new AgsTankMeta(2, "MS-I") },
        { 2, new AgsTankMeta(3, "MS-II") },
        { 3, new AgsTankMeta(1, "HSD") },
    };

    private static readonly Dictionary<int, string> AgsTankNumberToFuelType = new()
    {
        { 1, "MS-I"  },
        { 2, "MS-II" },
        { 3, "HSD"   },
    };

    private static readonly Dictionary<int, int> AgsTankNumberToTankId = new() { { 1, 2 }, { 2, 3 }, { 3, 1 } };

    // ─────────────────────────────────────────────────────────────────────────
    //  Regex patterns for extracting data from AGS PDF text
    // ─────────────────────────────────────────────────────────────────────────

    // Period timestamps: "From : 06:00" / "To : 14:00"  or  "Period : 06:00 To 14:00"
    private static readonly Regex PeriodFromRegex = new(
        @"(?:From|Start|Period\s*From)\s*[:\-]?\s*(\d{1,2}[:/]\d{2}(?:[:/]\d{2})?(?:\s*[AP]M)?(?:\s+\d{1,2}[-/]\d{1,2}[-/]\d{2,4})?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PeriodToRegex = new(
        @"(?:To|End|Period\s*To)\s*[:\-]?\s*(\d{1,2}[:/]\d{2}(?:[:/]\d{2})?(?:\s*[AP]M)?(?:\s+\d{1,2}[-/]\d{1,2}[-/]\d{2,4})?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PeriodRangeRegex = new(
        @"Period\s*:\s*(.+?)\s+to\s+(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Nozzle/DU reading line patterns:
    // e.g. "Nozzle  1   45230.50   45618.75   388.25"
    // e.g. "DU 1 Nozzle 1   45230.50   45618.75   388.25   20.00"
    // e.g. "Pump 2 Nozzle 1  OP: 45230.50  CL: 45618.75  Qty: 388.25"
    private static readonly Regex NozzleLineRegex = new(
        @"(?:DU\s*\d+\s*)?(?:Nozzle|Nz\.?)\s*(\d{1,2})\s+([\d,]+\.?\d*)\s+([\d,]+\.?\d*)\s+([\d,]+\.?\d*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Alternative: "1   45230.50   45618.75" (just number then two big decimals)
    private static readonly Regex NozzleSimpleRegex = new(
        @"^\s*(\d{1,2})\s+([\d,]{4,}\.?\d*)\s+([\d,]{4,}\.?\d*)",
        RegexOptions.Compiled);

    // BPCL Format: "1|1|2  1200492.96  1200487.96  5.00" (DU|Pump|Nozzle  Closing  Opening  Net)
    private static readonly Regex BpclNozzleRegex = new(
        @"^\s*(?:\d+)\|(?:\d+)\|(\d{1,2})\s+([\d,]+\.?\d*)\s+([\d,]+\.?\d*)\s+([\d,]+\.?\d*)",
        RegexOptions.Compiled);

    private static readonly Regex TankStockRowRegex = new(
        @"^\s*([123])\s+(MS|HSD|Petrol|Diesel|Motor\s*Spirit)\s+" +
        @"([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)" +
        @"(?:\s+([\d.]+))?(?:\s+([\d.]+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Tank dip readings:
    // e.g. "Tank 1  HSD   Opening: 1840mm  1840  12000.00  Closing: 1510mm  1510  8760.00"
    // e.g. "ATG Tank 1 (HSD)  Dip: 1840  Vol: 12000.0"
    private static readonly Regex TankLineRegex = new(
        @"(?:Tank|ATG|Probe)\s*(\d)\s*[:\(]?\s*(?:HSD|MS[\s\-]?(?:I{1,2}|1|2)|Petrol|Motor\s*Spirit)?[:\)]?\s*([\d,]+\.?\d*)\s+([\d,]+\.?\d*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Opening/Closing stock line
    private static readonly Regex OpeningStockRegex = new(
        @"(?:Opening|Op\.?)\s*(?:Stock|Volume|Vol\.?)?\s*[:\-]?\s*([\d,]+\.?\d*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ClosingStockRegex = new(
        @"(?:Closing|Cl\.?)\s*(?:Stock|Volume|Vol\.?)?\s*[:\-]?\s*([\d,]+\.?\d*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Fuel type detection
    private static readonly Regex HsdFuelRegex = new(@"\bHSD\b|\bDiesel\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MsFuelRegex  = new(@"\bMS\b|\bPetrol\b|\bMotor\s*Spirit\b|\bGasoline\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ─────────────────────────────────────────────────────────────────────────
    //  Public entry point
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<AgsShiftImportDto> ParseAgsReportAsync(string pdfFilePath, DateTime date, string shiftType)
    {
        return await Task.Run(() => ParseInternal(pdfFilePath, date, shiftType));
    }

    private AgsShiftImportDto ParseInternal(string pdfFilePath, DateTime date, string shiftType)
    {
        _logger.Information("Starting AGS PDF parse: {File} for {Date} Shift {Shift}", 
            Path.GetFileName(pdfFilePath), date.ToString("dd-MMM-yyyy"), shiftType);

        var dto = new AgsShiftImportDto
        {
            ImportDate  = date,
            ShiftType   = shiftType,
            PdfFileName = Path.GetFileName(pdfFilePath),
        };

        using var document = PdfDocument.Open(pdfFilePath);

        var allLines = new List<string>();
        foreach (var page in document.GetPages())
        {
            allLines.AddRange(GroupWordsIntoRows(page));
        }

        ExtractPeriod(allLines, dto);

        dto.NozzleReadings = ExtractNozzleReadings(document, dto.ParseLog);

        var tanksByAgs = new Dictionary<int, AgsTankStockDto>();
        var stockSummary = new Dictionary<string, ProductStockSummary>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in document.GetPages())
        {
            foreach (var tank in ExtractTankStock(page))
            {
                tanksByAgs[tank.AgsTankNumber] = tank;
            }

            // Extract opening stock only
            foreach (var kv in ExtractStockSummary(page))
            {
                stockSummary[kv.Key] = kv.Value;
            }
        }

        dto.TankStocks = tanksByAgs.Values.OrderBy(x => x.AgsTankNumber).ToList();
        PopulateOpeningStock(dto.TankStocks, stockSummary);

        dto.Summary = BuildSummary(dto);

        _logger.Information("AGS Parse complete — {NozzleCount} nozzles, {TankCount} tanks extracted",
            dto.NozzleReadings.Count, dto.TankStocks.Count);

        return dto;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Strategy 1: Group words into rows by Y-coordinate (±3pt tolerance)
    // ─────────────────────────────────────────────────────────────────────────

    private static List<string> GroupWordsIntoRows(Page page)
    {
        const double yTolerance = 3.0;

        // PdfPig words have bounding boxes
        var words = page.GetWords()
            .OrderByDescending(w => w.BoundingBox.Bottom)  // top of page first
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        var rows = new List<(double Y, List<Word> Words)>();

        foreach (var word in words)
        {
            double wordY = word.BoundingBox.Bottom;
            var existingRow = rows.FirstOrDefault(r => Math.Abs(r.Y - wordY) <= yTolerance);

            if (existingRow.Words == null)
            {
                rows.Add((wordY, new List<Word> { word }));
            }
            else
            {
                existingRow.Words.Add(word);
                // Re-sort words in row by X position
                existingRow.Words.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
            }
        }

        return rows
            .OrderByDescending(r => r.Y)
            .Select(r => string.Join("  ", r.Words.Select(w => w.Text)))
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Extract period timestamps
    // ─────────────────────────────────────────────────────────────────────────

    private void ExtractPeriod(List<string> lines, AgsShiftImportDto dto)
    {
        foreach (var line in lines)
        {
            var rangeMatch = PeriodRangeRegex.Match(line);
            if (rangeMatch.Success)
            {
                dto.PdfPeriodFrom ??= rangeMatch.Groups[1].Value.Trim();
                dto.PdfPeriodTo ??= rangeMatch.Groups[2].Value.Trim();
            }
            var fromMatch = PeriodFromRegex.Match(line);
            if (fromMatch.Success && dto.PdfPeriodFrom == null)
                dto.PdfPeriodFrom = fromMatch.Groups[1].Value.Trim();

            var toMatch = PeriodToRegex.Match(line);
            if (toMatch.Success && dto.PdfPeriodTo == null)
                dto.PdfPeriodTo = toMatch.Groups[1].Value.Trim();

            if (dto.PdfPeriodFrom != null && dto.PdfPeriodTo != null)
                break;
        }

        if (dto.PdfPeriodFrom != null)
            _logger.Information("AGS Period: {From} → {To}", dto.PdfPeriodFrom, dto.PdfPeriodTo ?? "unknown");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Extract Nozzle Readings (Method A)
    // ─────────────────────────────────────────────────────────────────────────

    private List<AgsNozzleReadingDto> ExtractNozzleReadings(PdfDocument pdf, List<string> parseLog)
    {
        var extracted = new List<ParsedNozzleReading>();

        foreach (var page in pdf.GetPages())
        {
            var msRows = ExtractNozzleBlock(page, "MS");
            var hsdRows = ExtractNozzleBlock(page, "HSD");

            if (msRows.Count > 0 || hsdRows.Count > 0)
            {
                parseLog.Add($"Method A page {page.Number}: MS={msRows.Count}, HSD={hsdRows.Count}");
            }

            extracted.AddRange(msRows);
            extracted.AddRange(hsdRows);
        }

        var mapped = new Dictionary<int, AgsNozzleReadingDto>();
        foreach (var row in extracted)
        {
            if (!AgsNozzleMap.TryGetValue(row.AgsKey, out var meta))
            {
                parseLog.Add($"WARNING: Missing AGS mapping for nozzle key '{row.AgsKey}'.");
                continue;
            }

            mapped[meta.PhysicalNozzle] = new AgsNozzleReadingDto
            {
                NozzleNumber = meta.PhysicalNozzle,
                PumpNumber = meta.PumpNumber,
                FuelType = meta.FuelType,
                OpeningReading = row.PreviousTotalizer,
                ClosingReading = row.CurrentTotalizer,
                TestingDeduction = 0
            };
        }

        var result = mapped.Values.OrderBy(x => x.NozzleNumber).ToList();
        _logger.Information("Extracted {Count} nozzle readings", result.Count);
        return result;
    }

    private List<ParsedNozzleReading> ExtractNozzleBlock(Page page, string productLabel)
    {
        var rows = GroupRowsDetailed(page);
        var result = new List<ParsedNozzleReading>();
        if (rows.Count == 0) return result;

        var productIndex = rows.FindIndex(r =>
            r.Text.Contains("PRODUCT", StringComparison.OrdinalIgnoreCase) &&
            r.Text.Contains(productLabel, StringComparison.OrdinalIgnoreCase));
        if (productIndex < 0) return result;

        var endIndex = rows.Count;
        for (int i = productIndex + 1; i < rows.Count; i++)
        {
            var text = rows[i].Text;
            if (text.Contains("NOZZLE SALES SUMMARY", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("TANK STOCK SUMMARY", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("STOCK SUMMARY", StringComparison.OrdinalIgnoreCase) ||
                (text.Contains("PRODUCT", StringComparison.OrdinalIgnoreCase) && !text.Contains(productLabel, StringComparison.OrdinalIgnoreCase)))
            {
                endIndex = i;
                break;
            }
        }

        var blockRows = rows.Skip(productIndex + 1).Take(endIndex - productIndex - 1).ToList();
        if (blockRows.Count == 0) return result;

        var keyRow = blockRows.FirstOrDefault(r => r.Words.Count(w => Regex.IsMatch(w.Text, @"^\d+\|\d+\|\d+$")) >= 2);
        if (keyRow == null) return result;

        var keyWords = keyRow.Words.Where(w => Regex.IsMatch(w.Text, @"^\d+\|\d+\|\d+$")).ToList();
        if (keyWords.Count == 0) return result;

        int currentRowIndex = blockRows.FindIndex(r => r.Text.Contains("Current", StringComparison.OrdinalIgnoreCase));
        int previousRowIndex = blockRows.FindIndex(r => r.Text.Contains("Previous", StringComparison.OrdinalIgnoreCase));
        int netRowIndex = blockRows.FindIndex(r => r.Text.Contains("Net", StringComparison.OrdinalIgnoreCase) && r.Text.Contains("Nozzle", StringComparison.OrdinalIgnoreCase));

        if (currentRowIndex < 0 || previousRowIndex < 0) return result;

        bool isMsBlock = productLabel.Equals("MS", StringComparison.OrdinalIgnoreCase);

        foreach (var key in keyWords)
        {
            decimal current;
            decimal previous;

            if (isMsBlock)
            {
                if (currentRowIndex + 1 >= blockRows.Count || previousRowIndex + 1 >= blockRows.Count) continue;
                var currentTop = FindWordInColumn(blockRows[currentRowIndex], key.BoundingBox.Left, t => Regex.IsMatch(t, @"^\d{4,}$"));
                var currentBottom = FindWordInColumn(blockRows[currentRowIndex + 1], key.BoundingBox.Left, t => t.Contains('.') && Regex.IsMatch(t, @"^\d{1,3}\.\d{2}$"));
                var previousTop = FindWordInColumn(blockRows[previousRowIndex], key.BoundingBox.Left, t => Regex.IsMatch(t, @"^\d{4,}$"));
                var previousBottom = FindWordInColumn(blockRows[previousRowIndex + 1], key.BoundingBox.Left, t => t.Contains('.') && Regex.IsMatch(t, @"^\d{1,3}\.\d{2}$"));

                if (currentTop == null || currentBottom == null || previousTop == null || previousBottom == null) continue;
                current = ReconstructSplitNumber(currentTop.Text, currentBottom.Text);
                previous = ReconstructSplitNumber(previousTop.Text, previousBottom.Text);
            }
            else
            {
                var currentWord = FindWordInColumn(blockRows[currentRowIndex], key.BoundingBox.Left, t => Regex.IsMatch(t, @"^\d+\.\d{2}$"));
                var previousWord = FindWordInColumn(blockRows[previousRowIndex], key.BoundingBox.Left, t => Regex.IsMatch(t, @"^\d+\.\d{2}$"));
                if (currentWord == null || previousWord == null) continue;

                if (!decimal.TryParse(currentWord.Text, out current)) continue;
                if (!decimal.TryParse(previousWord.Text, out previous)) continue;
            }

            var row = new ParsedNozzleReading
            {
                AgsKey = key.Text.Trim(),
                ProductLabel = productLabel,
                CurrentTotalizer = (double)current,
                PreviousTotalizer = (double)previous,
                NetNozzleSale = (double)(current - previous)
            };

            if (netRowIndex >= 0)
            {
                var netValueWord = netRowIndex + 1 < blockRows.Count
                    ? FindWordInColumn(blockRows[netRowIndex + 1], key.BoundingBox.Left, t => Regex.IsMatch(t, @"^-?\d+\.\d{2}$"))
                    : null;
                if (netValueWord != null && decimal.TryParse(netValueWord.Text, out var netFromPdf))
                {
                    row.NetNozzleSale = (double)netFromPdf;
                }
            }

            result.Add(row);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Extract Tank Stock Readings (Method B)
    // ─────────────────────────────────────────────────────────────────────────

    private List<AgsTankStockDto> ExtractTankStock(Page page)
    {
        var rows = GroupRowsDetailed(page);
        var result = new List<AgsTankStockDto>();

        // The tank stock table may span multiple pages. Page 1 has the
        // "TANK STOCK SUMMARY" header, but page 2 only carries the
        // continuation (column header + data rows). Accept the page if
        // it contains either the section title OR the column header row.
        bool hasTankStockSection = rows.Any(r =>
            r.Text.Contains("TANK STOCK SUMMARY", StringComparison.OrdinalIgnoreCase) ||
            (r.Text.Contains("TANK", StringComparison.OrdinalIgnoreCase) &&
             r.Text.Contains("PRODUCT", StringComparison.OrdinalIgnoreCase) &&
             r.Text.Contains("HEIGHT", StringComparison.OrdinalIgnoreCase) &&
             r.Text.Contains("VOLUME", StringComparison.OrdinalIgnoreCase)));

        if (!hasTankStockSection)
        {
            return result;
        }

        // Regex: TankNo  Product  Height  Volume  WaterHeight  WaterVolume  NetVolume
        // Allow optional trailing fields for flexibility
        var tankRowRegex = new Regex(
            @"^\s*([123])\s+(MS|HSD)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)",
            RegexOptions.IgnoreCase);

        foreach (var row in rows)
        {
            var match = tankRowRegex.Match(row.Text);
            if (!match.Success) continue;

            var agsTankNumber = int.Parse(match.Groups[1].Value);
            if (!AgsTankMap.TryGetValue(agsTankNumber, out var tankMeta)) continue;

            // The TANK STOCK table shows the current (closing) snapshot.
            // Opening stock is populated later from the STOCK SUMMARY section.
            var closingDip    = ParseDouble(match.Groups[3].Value);
            var closingVolume = ParseDouble(match.Groups[4].Value);
            var netVolume     = ParseDouble(match.Groups[7].Value);

            var tank = new AgsTankStockDto
            {
                AgsTankNumber     = agsTankNumber,
                TankNumber        = tankMeta.InternalTankId,
                FuelType          = tankMeta.FuelType,
                ClosingDipMM      = closingDip,
                ClosingStockLitres = netVolume > 0 ? netVolume : closingVolume,
                OpeningDipMM      = 0,
                OpeningStockLitres = 0,
                ReceiptLitres     = 0
            };

            result.Add(tank);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Extract Stock Summary (Method C)
    // ─────────────────────────────────────────────────────────────────────────

    private Dictionary<string, ProductStockSummary> ExtractStockSummary(Page page)
    {
        var rows = GroupRowsDetailed(page);
        var result = new Dictionary<string, ProductStockSummary>(StringComparer.OrdinalIgnoreCase);
        if (!rows.Any(r => r.Text.Contains("STOCK SUMMARY", StringComparison.OrdinalIgnoreCase)))
        {
            return result;
        }

        var ms = new ProductStockSummary { Product = "MS" };
        var hsd = new ProductStockSummary { Product = "HSD" };

        foreach (var row in rows)
        {
            if (!TryGetMsHsdPair(row.Text, out var msVal, out var hsdVal)) continue;

            if (row.Text.Contains("Opening", StringComparison.OrdinalIgnoreCase))
            {
                ms.OpeningStock = msVal;
                hsd.OpeningStock = hsdVal;
            }
            else if (row.Text.Contains("Receipt", StringComparison.OrdinalIgnoreCase))
            {
                ms.Receipt = msVal;
                hsd.Receipt = hsdVal;
            }
            else if (row.Text.Contains("Closing", StringComparison.OrdinalIgnoreCase))
            {
                ms.ClosingStock = msVal;
                hsd.ClosingStock = hsdVal;
            }
            else if (row.Text.Contains("Total Sale Based On Tank", StringComparison.OrdinalIgnoreCase))
            {
                ms.TankSale = msVal;
                hsd.TankSale = hsdVal;
            }
            else if (row.Text.Contains("Difference", StringComparison.OrdinalIgnoreCase))
            {
                ms.Variation = msVal;
                hsd.Variation = hsdVal;
            }
            else if (Regex.IsMatch(row.Text, @"^\s*PV\b", RegexOptions.IgnoreCase))
            {
                ms.PermissibleVariation = msVal;
                hsd.PermissibleVariation = hsdVal;
            }
        }

        if (ms.TankSale != 0 || hsd.TankSale != 0)
        {
            result["MS"] = ms;
            result["HSD"] = hsd;
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Extract Nozzle Sales Summary (Method D)
    // ─────────────────────────────────────────────────────────────────────────

    private Dictionary<string, ProductNozzleSummary> ExtractNozzleSalesSummary(Page page)
    {
        var rows = GroupRowsDetailed(page);
        var result = new Dictionary<string, ProductNozzleSummary>(StringComparer.OrdinalIgnoreCase);
        if (!rows.Any(r => r.Text.Contains("NOZZLE SALES SUMMARY", StringComparison.OrdinalIgnoreCase)))
        {
            return result;
        }

        foreach (var row in rows)
        {
            var match = Regex.Match(row.Text, @"^\s*(MS|HSD)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)", RegexOptions.IgnoreCase);
            if (!match.Success) continue;

            var product = match.Groups[1].Value.ToUpperInvariant();
            result[product] = new ProductNozzleSummary
            {
                Product = product,
                GrossNozzleSale = ParseDouble(match.Groups[2].Value),
                TwoTOil = ParseDouble(match.Groups[3].Value),
                TestingDeduction = ParseDouble(match.Groups[4].Value),
                NetTotalizerSale = ParseDouble(match.Groups[5].Value)
            };
        }

        return result;
    }

    /// <summary>
    /// Backfill opening stock on each tank from the STOCK SUMMARY section.
    /// The AGS "Tank Opening Stock Of All Tank(A)" row gives aggregate opening
    /// stock per product group (MS and HSD). For HSD there is a single tank,
    /// so the value maps directly. For MS there are two tanks (MS-I, MS-II);
    /// we distribute the total MS opening stock proportionally by closing stock.
    /// </summary>
    private static void PopulateOpeningStock(
        List<AgsTankStockDto> tanks,
        Dictionary<string, ProductStockSummary> stockSummary)
    {
        if (tanks.Count == 0 || stockSummary.Count == 0) return;

        // HSD: single tank, map directly
        if (stockSummary.TryGetValue("HSD", out var hsdSummary))
        {
            var hsdTank = tanks.FirstOrDefault(t => t.FuelType.Equals("HSD", StringComparison.OrdinalIgnoreCase));
            if (hsdTank != null)
            {
                hsdTank.OpeningStockLitres = hsdSummary.OpeningStock;
            }
        }

        // MS: the report combines MS-I and MS-II into a single "MS" total.
        // Distribute proportionally by closing stock ratio.
        if (stockSummary.TryGetValue("MS", out var msSummary))
        {
            var msTanks = tanks.Where(t => t.FuelType.StartsWith("MS", StringComparison.OrdinalIgnoreCase)).ToList();
            var totalClosing = msTanks.Sum(t => t.ClosingStockLitres);

            if (msTanks.Count == 1)
            {
                msTanks[0].OpeningStockLitres = msSummary.OpeningStock;
            }
            else if (totalClosing > 0)
            {
                foreach (var tank in msTanks)
                {
                    var ratio = tank.ClosingStockLitres / totalClosing;
                    tank.OpeningStockLitres = Math.Round(msSummary.OpeningStock * ratio, 2);
                }
            }
            else
            {
                // Can't proportion — split evenly
                var each = Math.Round(msSummary.OpeningStock / msTanks.Count, 2);
                foreach (var tank in msTanks)
                {
                    tank.OpeningStockLitres = each;
                }
            }
        }
    }



    // ─────────────────────────────────────────────────────────────────────────
    //  Build summary from parsed nozzle/tank data
    // ─────────────────────────────────────────────────────────────────────────

    private static AgsShiftSummaryDto BuildSummary(AgsShiftImportDto dto)
    {
        var summary = new AgsShiftSummaryDto();

        // Preview totals must be based on closing-opening differences for all nozzles.
        summary.TotalHsdSaleLitres  = dto.NozzleReadings.Where(n => n.FuelType == "HSD"  ).Sum(n => n.SaleLitres);
        summary.TotalMsISaleLitres  = dto.NozzleReadings.Where(n => n.FuelType == "MS-I" ).Sum(n => n.SaleLitres);
        summary.TotalMsIISaleLitres = dto.NozzleReadings.Where(n => n.FuelType == "MS-II").Sum(n => n.SaleLitres);

        var hsdTank  = dto.TankStocks.FirstOrDefault(t => t.FuelType == "HSD");
        var msITank  = dto.TankStocks.FirstOrDefault(t => t.FuelType == "MS-I");
        var msIITank = dto.TankStocks.FirstOrDefault(t => t.FuelType == "MS-II");

        if (hsdTank != null)
        {
            summary.HsdOpeningStock = hsdTank.OpeningStockLitres;
            summary.HsdClosingStock = hsdTank.ClosingStockLitres;
        }
        if (msITank != null)
        {
            summary.MsIOpeningStock = msITank.OpeningStockLitres;
            summary.MsIClosingStock = msITank.ClosingStockLitres;
        }
        if (msIITank != null)
        {
            summary.MsIIOpeningStock = msIITank.OpeningStockLitres;
            summary.MsIIClosingStock = msIITank.ClosingStockLitres;
        }

        // Variances: nozzle total vs tank dispensed
        double hsdDispensed  = hsdTank?.FuelDispensedLitres ?? 0;
        double msIDispensed  = msITank?.FuelDispensedLitres ?? 0;
        double msIIDispensed = msIITank?.FuelDispensedLitres ?? 0;

        summary.VariationHsd  = hsdDispensed  > 0 ? Math.Abs(summary.TotalHsdSaleLitres  - hsdDispensed)  / hsdDispensed  * 100 : 0;
        summary.VariationMsI  = msIDispensed  > 0 ? Math.Abs(summary.TotalMsISaleLitres  - msIDispensed)  / msIDispensed  * 100 : 0;
        summary.VariationMsII = msIIDispensed > 0 ? Math.Abs(summary.TotalMsIISaleLitres - msIIDispensed) / msIIDispensed * 100 : 0;

        return summary;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static double ParseDouble(string s)
    {
        s = s.Replace(",", "").Trim();
        return double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
    }

    public async Task<string> DumpPdfTextAsync(string pdfFilePath, string outputDir)
    {
        var sb = new StringBuilder();
        using var pdf = PdfDocument.Open(pdfFilePath);
        foreach (var page in pdf.GetPages())
        {
            sb.AppendLine($"=== PAGE {page.Number} ===");
            var words = page.GetWords()
                .OrderBy(w => -w.BoundingBox.Top)
                .ThenBy(w => w.BoundingBox.Left)
                .ToList();

            foreach (var word in words)
            {
                sb.AppendLine($"[X:{word.BoundingBox.Left:F1} Y:{word.BoundingBox.Top:F1}] '{word.Text}'");
            }
        }

        Directory.CreateDirectory(outputDir);
        var outputFile = Path.Combine(outputDir, $"ags_dump_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        await File.WriteAllTextAsync(outputFile, sb.ToString(), Encoding.UTF8);
        return outputFile;
    }

    private static List<RowData> GroupRowsDetailed(Page page)
    {
        const double yTolerance = 3.0;
        var words = page.GetWords()
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        var rows = new List<RowData>();
        foreach (var word in words)
        {
            var y = word.BoundingBox.Bottom;
            var row = rows.FirstOrDefault(r => Math.Abs(r.Y - y) <= yTolerance);
            if (row == null)
            {
                row = new RowData { Y = y };
                rows.Add(row);
            }

            row.Words.Add(word);
        }

        foreach (var row in rows)
        {
            row.Words = row.Words.OrderBy(w => w.BoundingBox.Left).ToList();
        }

        return rows.OrderByDescending(r => r.Y).ToList();
    }

    private static Word? FindWordInColumn(RowData row, double columnX, Func<string, bool> predicate, double tolerance = 5.0)
    {
        return row.Words
            .Where(w => Math.Abs(w.BoundingBox.Left - columnX) <= tolerance && predicate(w.Text))
            .OrderBy(w => Math.Abs(w.BoundingBox.Left - columnX))
            .FirstOrDefault()
            ?? row.Words
                .Where(w => Math.Abs(w.BoundingBox.Left - columnX) <= (tolerance + 2.0) && predicate(w.Text))
                .OrderBy(w => Math.Abs(w.BoundingBox.Left - columnX))
                .FirstOrDefault();
    }

    private static decimal ReconstructSplitNumber(string topPart, string bottomPart)
    {
        var combined = topPart.Trim() + bottomPart.Trim();
        return decimal.TryParse(combined, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : 0m;
    }

    private static bool TryGetMsHsdPair(string line, out double ms, out double hsd)
    {
        ms = 0;
        hsd = 0;
        var matches = Regex.Matches(line, @"-?\d+(?:\.\d+)?");
        if (matches.Count < 2) return false;

        ms = ParseDouble(matches[matches.Count - 2].Value);
        hsd = ParseDouble(matches[matches.Count - 1].Value);
        return true;
    }

    private sealed class RowData
    {
        public double Y { get; set; }
        public List<Word> Words { get; set; } = new();
        public string Text => string.Join(" ", Words.Select(w => w.Text));
    }

    private sealed class ParsedNozzleReading
    {
        public string AgsKey { get; set; } = string.Empty;
        public string ProductLabel { get; set; } = string.Empty;
        public double CurrentTotalizer { get; set; }
        public double PreviousTotalizer { get; set; }
        public double NetNozzleSale { get; set; }
    }

    private sealed class ProductNozzleSummary
    {
        public string Product { get; set; } = string.Empty;
        public double GrossNozzleSale { get; set; }
        public double TestingDeduction { get; set; }
        public double TwoTOil { get; set; }
        public double NetTotalizerSale { get; set; }
    }

    private sealed class ProductStockSummary
    {
        public string Product { get; set; } = string.Empty;
        public double OpeningStock { get; set; }
        public double Receipt { get; set; }
        public double ClosingStock { get; set; }
        public double TankSale { get; set; }
        public double Variation { get; set; }
        public double PermissibleVariation { get; set; }
    }
}
