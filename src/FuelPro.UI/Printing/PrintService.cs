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

    private string LoadDayTotalTemplate()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("DayTotalPrintTemplate.html"));

        if (resourceName != null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var path = Path.Combine(exeDir, "Printing", "DayTotalPrintTemplate.html");

        if (!File.Exists(path))
        {
            var srcPath = Path.Combine(exeDir, "..", "..", "..", "..",
                "FuelPro.UI", "Printing", "DayTotalPrintTemplate.html");
            path = Path.GetFullPath(srcPath);
        }

        if (!File.Exists(path))
            throw new FileNotFoundException("Day Total print template not found: " + path);

        return File.ReadAllText(path, Encoding.UTF8);
    }

    /// <summary>
    /// Loads the HTML template — tries embedded resource first, then file fallback.
    /// </summary>
    private string LoadTemplate()
    {
        // Try embedded resource first
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("FinalCalculationPrintTemplate.html"));

        if (resourceName != null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            _logger.Debug("Loaded print template from embedded resource: {Name}", resourceName);
            return reader.ReadToEnd();
        }

        // Fallback: file next to exe
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var path = Path.Combine(exeDir, "Printing", "FinalCalculationPrintTemplate.html");

        if (!File.Exists(path))
        {
            // Dev fallback: source directory
            var srcPath = Path.Combine(
                exeDir, "..", "..", "..", "..",
                "FuelPro.UI", "Printing", "FinalCalculationPrintTemplate.html");
            path = Path.GetFullPath(srcPath);
        }

        if (!File.Exists(path))
            throw new FileNotFoundException("Print template not found. Expected: " + path);

        _logger.Debug("Loaded print template from file: {Path}", path);
        return File.ReadAllText(path, Encoding.UTF8);
    }
}
