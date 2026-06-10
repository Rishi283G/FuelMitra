using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using FuelPro.Core.DTOs;
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

    /// <summary>
    /// Builds the temp HTML, opens in default browser for printing.
    /// Call from the UI thread.
    /// </summary>
    public void PrintFinalCalculation(FinalCalcPrintData data)
    {
        try
        {
            _logger.Information("PrintFinalCalculation started for {Date} Shift {Shift}",
                data.Date, data.ShiftLabel);

            // STEP A: Read template
            var templateHtml = LoadTemplate();

            // STEP B: Serialize data to JSON
            var json = JsonSerializer.Serialize(data, _jsonOptions);

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
                $"VKDPrint_{DateTime.Now:yyyyMMddHHmmss}.html");

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
    public void PrintDayTotal(object data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            var templateHtml = LoadDayTotalTemplate();

            if (!templateHtml.Contains(MARKER))
            {
                MessageBox.Show("Day Total print template is outdated.",
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var finalHtml = templateHtml.Replace(MARKER, json);
            var tempFile = Path.Combine(Path.GetTempPath(),
                $"VKDDayTotal_{DateTime.Now:yyyyMMddHHmmss}.html");

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
    /// Prints an individual DSM Entry Sheet (9 sections: header, nozzles, payments,
    /// cash denomination, creditors, expenses, testing, reconciliation, signatures).
    /// </summary>
    public void PrintDsmSheet(FuelPro.Core.DTOs.DsmSheetPrintData data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            var templateHtml = LoadNamedTemplate("DsmSheetPrintTemplate.html");

            if (!templateHtml.Contains(MARKER))
            {
                MessageBox.Show("DSM Sheet print template is outdated.",
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var finalHtml = templateHtml.Replace(MARKER, json);
            var tempFile = Path.Combine(Path.GetTempPath(),
                $"VKDDsmSheet_{DateTime.Now:yyyyMMddHHmmss}.html");

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
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            var templateHtml = LoadNamedTemplate("ShiftSummaryPrintTemplate.html");

            if (!templateHtml.Contains(MARKER))
            {
                MessageBox.Show("Shift Summary print template is outdated.",
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var finalHtml = templateHtml.Replace(MARKER, json);
            var tempFile = Path.Combine(Path.GetTempPath(),
                $"VKDShiftSummary_{DateTime.Now:yyyyMMddHHmmss}.html");

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
}
