using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using FuelPro.Core.DTOs;
using FuelPro.Data;
using Serilog;

namespace FuelPro.UI.Printing;

/// <summary>
/// Handles the end-to-end print flow:
///   1. Load HTML template (embedded resource or file fallback).
///   2. Inject JSON data by replacing the INJECT_JSON_HERE marker.
///   3. Write to a temp file.
///   4. Open in the user's default browser (Chrome/Edge — full CSS support).
///   5. The template's toolbar has a Print button; user clicks it.
///   6. Temp file auto-cleaned after 5 minutes.
/// </summary>
public class PrintService
{
    private readonly ILogger _logger = Log.ForContext<PrintService>();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private const string MARKER = "/* INJECT_JSON_HERE */{}";

    private string GetSerializedJsonWithLogoAndStationName(object data)
    {
        // 1. Get station name
        string stationName = "Mitali Service Station";
        try
        {
            var dbContext = App.Services?.GetService(typeof(FuelProDbContext)) as FuelProDbContext;
            if (dbContext != null)
            {
                var settings = dbContext.Settings.FirstOrDefault();
                if (settings != null)
                {
                    stationName = settings.StationDisplayName;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get station display name from DB for print");
        }

        // 2. Get base64 logo (light logo by default for printing since templates use white background)
        string logoBase64 = "";
        try
        {
            var logoPath = App.GetLogoPath(isDark: false);
            if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
            {
                var bytes = File.ReadAllBytes(logoPath);
                var ext = Path.GetExtension(logoPath).TrimStart('.').ToLower();
                if (ext == "jpg") ext = "jpeg";
                logoBase64 = $"data:image/{ext};base64,{Convert.ToBase64String(bytes)}";
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to encode logo to base64 for print");
        }

        // 3. Serialize data, parse it, and inject logo and stationName
        try
        {
            var serialized = JsonSerializer.Serialize(data, _jsonOptions);
            using var doc = JsonDocument.Parse(serialized);
            var dictionary = new Dictionary<string, object>();

            // Populate from existing properties
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                dictionary[prop.Name] = prop.Value;
            }

            // Inject or override
            dictionary["stationName"] = stationName;
            dictionary["logo"] = logoBase64;

            return JsonSerializer.Serialize(dictionary, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to inject logo and stationName to json");
            return JsonSerializer.Serialize(data, _jsonOptions);
        }
    }

    /// <summary>
    /// Builds the temp HTML, opens in default browser for printing.
    /// Call from the UI thread.
    /// </summary>
    public void PrintFinalCalculation(ShiftReportDto data)
    {
        try
        {
            _logger.Information("PrintFinalCalculation started for {Date} Shift {Shift}",
                data.DateString, data.ShiftLabel);

            // STEP A: Read template
            var templateHtml = LoadTemplate();

            // STEP B: Serialize data to JSON with logo and stationName
            var json = GetSerializedJsonWithLogoAndStationName(data);

            // STEP C: Inject JSON into template
            if (!templateHtml.Contains(MARKER))
            {
                _logger.Error("Print template injection marker not found. Template may be outdated.");
                MessageBox.Show(
                    "Print template is outdated. Please reinstall the application.",
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var finalHtml = templateHtml.Replace(MARKER, json);

            // STEP D: Write to unique temp file
            var tempFile = Path.Combine(
                Path.GetTempPath(),
                $"PyroSyncPrint_{DateTime.Now:yyyyMMddHHmmss}.html");

            File.WriteAllText(tempFile, finalHtml, Encoding.UTF8);
            _logger.Information("Print temp file written: {Path}", tempFile);

            // STEP E: Open in default browser (Chrome/Edge — full CSS support)
            Process.Start(new ProcessStartInfo
            {
                FileName = tempFile,
                UseShellExecute = true
            });

            // STEP F: Schedule temp file cleanup after 5 minutes
            Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); }
                catch { /* ignore cleanup errors */ }
            });

            _logger.Information("PrintFinalCalculation opened in browser: {File}", tempFile);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PrintFinalCalculation failed");
            MessageBox.Show(
                $"Print failed.\n\nError: {ex.Message}\n\nSee log for details.",
                "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Prints the Day Total report by injecting JSON into the Day Total template.
    /// </summary>
    public void PrintDayTotal(DayReportDto data)
    {
        try
        {
            var json = GetSerializedJsonWithLogoAndStationName(data);
            var templateHtml = LoadDayTotalTemplate();

            if (!templateHtml.Contains(MARKER))
            {
                MessageBox.Show("Day Total print template is outdated.",
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var finalHtml = templateHtml.Replace(MARKER, json);
            var tempFile = Path.Combine(Path.GetTempPath(),
                $"PyroSyncDayTotal_{DateTime.Now:yyyyMMddHHmmss}.html");

            File.WriteAllText(tempFile, finalHtml, Encoding.UTF8);

            Process.Start(new ProcessStartInfo { FileName = tempFile, UseShellExecute = true });

            Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PrintDayTotal failed");
            MessageBox.Show($"Print failed.\n\nError: {ex.Message}",
                "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Prints the Card Settlement report.
    /// </summary>
    public void PrintCardSettlement(object data)
    {
        try
        {
            var json = GetSerializedJsonWithLogoAndStationName(data);
            var templateHtml = LoadNamedTemplate("CardSettlementPrintTemplate.html");

            if (!templateHtml.Contains(MARKER))
            {
                MessageBox.Show("Card Settlement print template is outdated.",
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var finalHtml = templateHtml.Replace(MARKER, json);
            var tempFile = Path.Combine(Path.GetTempPath(),
                $"PyroSyncCardSettlement_{DateTime.Now:yyyyMMddHHmmss}.html");

            File.WriteAllText(tempFile, finalHtml, Encoding.UTF8);
            Process.Start(new ProcessStartInfo { FileName = tempFile, UseShellExecute = true });

            Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PrintCardSettlement failed");
            MessageBox.Show($"Print failed.\n\nError: {ex.Message}",
                "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Prints an individual DSM Entry Sheet (9 sections: header, nozzles, payments,
    /// cash denomination, creditors, expenses, testing, reconciliation, signatures).
    /// </summary>
    public void PrintDsmSheet(FuelPro.Core.DTOs.DsmSheetPrintData data)
    {
        try
        {
            var json = GetSerializedJsonWithLogoAndStationName(data);
            var templateHtml = LoadNamedTemplate("DsmSheetPrintTemplate.html");

            if (!templateHtml.Contains(MARKER))
            {
                MessageBox.Show("DSM Sheet print template is outdated.",
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var finalHtml = templateHtml.Replace(MARKER, json);
            var tempFile = Path.Combine(Path.GetTempPath(),
                $"PyroSyncDsmSheet_{DateTime.Now:yyyyMMddHHmmss}.html");

            File.WriteAllText(tempFile, finalHtml, Encoding.UTF8);
            Process.Start(new ProcessStartInfo { FileName = tempFile, UseShellExecute = true });

            Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PrintDsmSheet failed");
            MessageBox.Show($"Print failed.\n\nError: {ex.Message}",
                "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Prints the Shift Summary report — lists all DSM entries for the shift with totals.
    /// </summary>
    public void PrintShiftSummary(FuelPro.Core.DTOs.ShiftSummaryPrintData data)
    {
        try
        {
            var json = GetSerializedJsonWithLogoAndStationName(data);
            var templateHtml = LoadNamedTemplate("ShiftSummaryPrintTemplate.html");

            if (!templateHtml.Contains(MARKER))
            {
                MessageBox.Show("Shift Summary print template is outdated.",
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var finalHtml = templateHtml.Replace(MARKER, json);
            var tempFile = Path.Combine(Path.GetTempPath(),
                $"PyroSyncShiftSummary_{DateTime.Now:yyyyMMddHHmmss}.html");

            File.WriteAllText(tempFile, finalHtml, Encoding.UTF8);
            Process.Start(new ProcessStartInfo { FileName = tempFile, UseShellExecute = true });

            Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PrintShiftSummary failed");
            MessageBox.Show($"Print failed.\n\nError: {ex.Message}",
                "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Prints the Monthly Profit &amp; Loss Statement report.
    /// </summary>
    public void PrintMonthlyPL(object data)
    {
        try
        {
            var json = GetSerializedJsonWithLogoAndStationName(data);
            var templateHtml = LoadNamedTemplate("MonthlyPLPrintTemplate.html");

            if (!templateHtml.Contains(MARKER))
            {
                MessageBox.Show("Monthly P&L print template is outdated.",
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var finalHtml = templateHtml.Replace(MARKER, json);
            var tempFile = Path.Combine(Path.GetTempPath(),
                $"PyroSyncMonthlyPL_{DateTime.Now:yyyyMMddHHmmss}.html");

            File.WriteAllText(tempFile, finalHtml, Encoding.UTF8);
            Process.Start(new ProcessStartInfo { FileName = tempFile, UseShellExecute = true });

            Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PrintMonthlyPL failed");
            MessageBox.Show($"Print failed.\n\nError: {ex.Message}",
                "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void PrintOilDefSummary(object data)
    {
        try
        {
            var json = GetSerializedJsonWithLogoAndStationName(data);
            var templateHtml = LoadNamedTemplate("OilDefSummaryPrintTemplate.html");

            if (!templateHtml.Contains(MARKER))
            {
                MessageBox.Show("Oil & DEF Summary print template is outdated.",
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var finalHtml = templateHtml.Replace(MARKER, json);
            var tempFile = Path.Combine(Path.GetTempPath(),
                $"PyroSyncOilDefSummary_{DateTime.Now:yyyyMMddHHmmss}.html");

            File.WriteAllText(tempFile, finalHtml, Encoding.UTF8);
            Process.Start(new ProcessStartInfo { FileName = tempFile, UseShellExecute = true });

            Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PrintOilDefSummary failed");
            MessageBox.Show($"Print failed.\n\nError: {ex.Message}",
                "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string LoadDayTotalTemplate()
    {
        return LoadNamedTemplate("DayTotalPrintTemplate.html");
    }

    /// <summary>
    /// Generic template loader — tries embedded resource first, then file fallback.
    /// </summary>
    private string LoadNamedTemplate(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName));

        if (resourceName != null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var path = Path.Combine(exeDir, "Printing", fileName);

        if (!File.Exists(path))
        {
            var srcPath = Path.Combine(exeDir, "..", "..", "..", "..",
                "FuelPro.UI", "Printing", fileName);
            path = Path.GetFullPath(srcPath);
        }

        if (!File.Exists(path))
            throw new FileNotFoundException($"Print template not found: {fileName}", path);

        return File.ReadAllText(path, Encoding.UTF8);
    }

    /// <summary>
    /// Loads the FinalCalculation HTML template — delegates to generic loader.
    /// </summary>
    private string LoadTemplate()
    {
        _logger.Debug("Loading FinalCalculationPrintTemplate.html");
        return LoadNamedTemplate("FinalCalculationPrintTemplate.html");
    }

    public void PrintDebtorLedger(DebtorLedgerPrintData data)
    {
        try
        {
            var json = GetSerializedJsonWithLogoAndStationName(data);
            var templateHtml = LoadNamedTemplate("DebtorLedgerPrintTemplate.html");

            if (!templateHtml.Contains(MARKER))
            {
                MessageBox.Show("Debtor Ledger print template is outdated.",
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var finalHtml = templateHtml.Replace(MARKER, json);
            var tempFile = Path.Combine(Path.GetTempPath(),
                $"PyroSyncDebtorLedger_{DateTime.Now:yyyyMMddHHmmss}.html");

            File.WriteAllText(tempFile, finalHtml, Encoding.UTF8);
            Process.Start(new ProcessStartInfo { FileName = tempFile, UseShellExecute = true });

            Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PrintDebtorLedger failed");
            MessageBox.Show($"Print failed.\n\nError: {ex.Message}",
                "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void PrintGenericGrid(GenericGridPrintData data)
    {
        try
        {
            var json = GetSerializedJsonWithLogoAndStationName(data);
            var templateHtml = LoadNamedTemplate("GenericGridPrintTemplate.html");

            if (!templateHtml.Contains(MARKER))
            {
                MessageBox.Show("Generic print template is outdated.",
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var finalHtml = templateHtml.Replace(MARKER, json);
            var tempFile = Path.Combine(Path.GetTempPath(),
                $"PyroSyncReport_{DateTime.Now:yyyyMMddHHmmss}.html");

            File.WriteAllText(tempFile, finalHtml, Encoding.UTF8);
            Process.Start(new ProcessStartInfo { FileName = tempFile, UseShellExecute = true });

            Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PrintGenericGrid failed");
            MessageBox.Show($"Print failed.\n\nError: {ex.Message}",
                "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
