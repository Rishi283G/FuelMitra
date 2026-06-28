using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models.AGS;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Serilog;

namespace FuelPro.UI.ViewModels;

public partial class AgsImportViewModel : ObservableObject
{
    private readonly IAgsImportService _agsService;
    private readonly IAgsDailyAggregationService _aggregationService;
    private readonly AgsImportValidator _validator;
    private readonly IAgsImportRepository _repo;
    private readonly ILogger _logger = Log.ForContext<AgsImportViewModel>();

    // ─── Step 1: Upload inputs ───────────────────────────────────────────────
    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private string _selectedShift = "A";
    [ObservableProperty] private string _selectedPdfPath = string.Empty;
    [ObservableProperty] private string _selectedPdfFileName = string.Empty;

    // ─── Step 2: Preview state ───────────────────────────────────────────────
    [ObservableProperty] private bool _isPreviewVisible;
    [ObservableProperty] private bool _hasBlockingErrors;
    [ObservableProperty] private string _previewStatusText = string.Empty;

    [ObservableProperty] private ObservableCollection<AgsNozzleReadingDto> _parsedNozzleReadings = new();
    [ObservableProperty] private ObservableCollection<AgsTankStockDto>     _parsedTankStocks     = new();
    [ObservableProperty] private ObservableCollection<ImportWarning>        _validationWarnings   = new();
    [ObservableProperty] private ObservableCollection<string>               _parseLogEntries      = new();

    // Summary totals for preview header cards
    [ObservableProperty] private double _previewHsdTotal;
    [ObservableProperty] private double _previewMsITotal;
    [ObservableProperty] private double _previewMsIITotal;
    [ObservableProperty] private string _previewPeriod = string.Empty;

    // ─── Import history ──────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<AgsImportHistoryDto> _importHistory = new();

    // ─── UI state ────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyMessage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    private AgsShiftImportDto? _parsedDto;

    public AgsImportViewModel()
    {
        _agsService         = App.Services.GetRequiredService<IAgsImportService>();
        _aggregationService = App.Services.GetRequiredService<IAgsDailyAggregationService>();
        _validator          = App.Services.GetRequiredService<AgsImportValidator>();
        _repo               = App.Services.GetRequiredService<IAgsImportRepository>();

        _ = LoadHistoryAsync();
    }

    // ─── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void BrowsePdf()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select AGS Field Officer Report",
            Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
        };

        if (dlg.ShowDialog() == true)
        {
            SelectedPdfPath     = dlg.FileName;
            SelectedPdfFileName = Path.GetFileName(dlg.FileName);
            IsPreviewVisible    = false;
            ParsedNozzleReadings.Clear();
            ParsedTankStocks.Clear();
            ValidationWarnings.Clear();
            ParseLogEntries.Clear();
            StatusMessage = $"PDF selected: {SelectedPdfFileName}";
        }
    }

    [RelayCommand]
    private async Task ParsePdfAsync()
    {
        if (string.IsNullOrEmpty(SelectedPdfPath))
        {
            StatusMessage = "Please select a PDF file first.";
            return;
        }

        if (!File.Exists(SelectedPdfPath))
        {
            StatusMessage = "Selected PDF file not found.";
            return;
        }

        try
        {
            IsBusy = true;
            BusyMessage = "Parsing AGS PDF…";
            IsPreviewVisible = false;

            _parsedDto = await _agsService.ParseAgsReportAsync(SelectedPdfPath, SelectedDate, SelectedShift);
            _parsedDto.ImportedBy = "System"; // DsmName removed, default to System or leave empty

            // Check for existing import of same shift
            var existingResult = await _repo.GetActiveShiftImportAsync(SelectedDate, SelectedShift);
            bool alreadyExists = existingResult.Success && existingResult.Data != null;

            // Validate
            var warnings = _validator.Validate(_parsedDto, alreadyExists);

            // Populate UI
            ParsedNozzleReadings = new ObservableCollection<AgsNozzleReadingDto>(_parsedDto.NozzleReadings);
            ParsedTankStocks     = new ObservableCollection<AgsTankStockDto>(_parsedDto.TankStocks);
            ValidationWarnings   = new ObservableCollection<ImportWarning>(warnings);
            ParseLogEntries      = new ObservableCollection<string>(_parsedDto.ParseLog);

            PreviewHsdTotal  = _parsedDto.Summary.TotalHsdSaleLitres;
            PreviewMsITotal  = _parsedDto.Summary.TotalMsISaleLitres;
            PreviewMsIITotal = _parsedDto.Summary.TotalMsIISaleLitres;
            PreviewPeriod    = _parsedDto.PdfPeriodFrom != null
                ? $"Period: {_parsedDto.PdfPeriodFrom} → {_parsedDto.PdfPeriodTo}"
                : $"Shift {SelectedShift} — {SelectedDate:dd-MMM-yyyy}";

            HasBlockingErrors = _validator.HasBlockingErrors(warnings);
            IsPreviewVisible  = true;

            PreviewStatusText = _parsedDto.NozzleReadings.Count > 0
                ? $"✅ {_parsedDto.NozzleReadings.Count} nozzle readings extracted. Review data below."
                : "⚠️ No nozzle readings extracted — check log for details.";

            StatusMessage = "";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to parse AGS PDF: {Path}", SelectedPdfPath);
            StatusMessage = $"Parse failed: {ex.Message}";
            IsPreviewVisible = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Reupload()
    {
        IsPreviewVisible = false;
        _parsedDto = null;
        ParsedNozzleReadings.Clear();
        ParsedTankStocks.Clear();
        ValidationWarnings.Clear();
        ParseLogEntries.Clear();
        StatusMessage = "Select a new PDF to parse.";
    }

    [RelayCommand]
    private async Task ConfirmSaveAsync()
    {
        if (_parsedDto == null)
        {
            StatusMessage = "No parsed data to save. Please parse a PDF first.";
            return;
        }

        if (HasBlockingErrors)
        {
            StatusMessage = "Cannot save — there are blocking errors. Please re-upload a valid PDF.";
            return;
        }

        try
        {
            IsBusy = true;
            BusyMessage = "Saving import…";

            // Soft-delete any existing active import for this date+shift
            var existing = await _repo.GetActiveShiftImportAsync(SelectedDate, SelectedShift);
            if (existing.Success && existing.Data != null)
                await _repo.SoftDeleteShiftImportAsync(existing.Data.AgsShiftImportId);

            // Build entity from DTO
            var entity = BuildEntity(_parsedDto);
            var saveResult = await _repo.SaveShiftImportAsync(entity);

            if (!saveResult.Success)
            {
                StatusMessage = $"Save failed: {saveResult.Error}";
                return;
            }

            // Re-aggregate daily summary
            var shiftsResult = await _repo.GetShiftsForDateAsync(SelectedDate);
            if (shiftsResult.Success)
            {
                var summary = _aggregationService.Aggregate(SelectedDate, shiftsResult.Data!);
                await _repo.SaveDailySummaryAsync(summary);
            }

            StatusMessage = $"✅ Shift {SelectedShift} imported successfully for {SelectedDate:dd-MMM-yyyy}.";
            IsPreviewVisible = false;
            _parsedDto = null;
            SelectedPdfPath = "";
            SelectedPdfFileName = "";
            ParsedNozzleReadings.Clear();
            ParsedTankStocks.Clear();
            ValidationWarnings.Clear();
            ParseLogEntries.Clear();

            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save AGS import");
            StatusMessage = $"Save error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportParseDumpAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedPdfPath) || !File.Exists(SelectedPdfPath))
        {
            StatusMessage = "Select a valid PDF before exporting parse dump.";
            return;
        }

        try
        {
            IsBusy = true;
            BusyMessage = "Exporting parser debug dump…";
            var logsDir = Path.Combine(AppContext.BaseDirectory, "Logs");
            var dumpPath = await _agsService.DumpPdfTextAsync(SelectedPdfPath, logsDir);
            StatusMessage = $"Parser debug dump exported: {dumpPath}";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export parser dump for {Path}", SelectedPdfPath);
            StatusMessage = $"Parser dump failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReimportAsync(AgsImportHistoryDto dto)
    {
        // Pre-fill fields from history row and let user re-select a PDF
        SelectedDate  = dto.ImportDate;
        SelectedShift = dto.ShiftType;
        Reupload();
        StatusMessage = $"Ready to re-import Shift {dto.ShiftType} for {dto.ImportDate:dd-MMM-yyyy}. Please select the new PDF.";
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DeleteImportAsync(AgsImportHistoryDto dto)
    {
        if (dto == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete the imported report for date {dto.ImportDate:dd-MMM-yyyy} Shift {dto.ShiftType}?\nThis action cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            IsBusy = true;
            BusyMessage = "Deleting import…";

            var deleteResult = await _repo.SoftDeleteShiftImportAsync(dto.AgsShiftImportId);
            if (!deleteResult.Success)
            {
                StatusMessage = $"Delete failed: {deleteResult.Error}";
                return;
            }

            // Re-aggregate daily summary for that date
            var shiftsResult = await _repo.GetShiftsForDateAsync(dto.ImportDate);
            if (shiftsResult.Success)
            {
                var summary = _aggregationService.Aggregate(dto.ImportDate, shiftsResult.Data!);
                await _repo.SaveDailySummaryAsync(summary);
            }

            StatusMessage = $"✅ Shift {dto.ShiftType} import for {dto.ImportDate:dd-MMM-yyyy} has been deleted.";
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete import {Id}", dto.AgsShiftImportId);
            StatusMessage = $"Delete error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }


    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        try
        {
            var result = await _repo.GetImportHistoryAsync(30);
            if (result.Success && result.Data != null)
            {
                ImportHistory = new ObservableCollection<AgsImportHistoryDto>(
                    result.Data.Select(x => new AgsImportHistoryDto
                    {
                        AgsShiftImportId = x.AgsShiftImportId,
                        ImportDate       = x.ImportDate,
                        ShiftType        = x.ShiftType,
                        ImportedBy       = x.ImportedBy,
                        PdfFileName      = x.PdfFileName,
                        TotalHsdLitres   = x.TotalHsdLitres,
                        TotalMsILitres   = x.TotalMsILitres,
                        TotalMsIILitres  = x.TotalMsIILitres,
                        ImportedAt       = x.ImportedAt,
                    }));
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load AGS import history");
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static AgsShiftImport BuildEntity(AgsShiftImportDto dto)
    {
        var entity = new AgsShiftImport
        {
            ImportDate      = dto.ImportDate.Date,
            ShiftType       = dto.ShiftType,
            PdfFileName     = dto.PdfFileName,
            ImportedBy      = dto.ImportedBy,
            ImportedAt      = DateTime.Now,
            IsActive        = true,
            PdfPeriodFrom   = dto.PdfPeriodFrom,
            PdfPeriodTo     = dto.PdfPeriodTo,
            TotalHsdLitres  = dto.Summary.TotalHsdSaleLitres,
            TotalMsILitres  = dto.Summary.TotalMsISaleLitres,
            TotalMsIILitres = dto.Summary.TotalMsIISaleLitres,
            HsdOpeningStock  = dto.Summary.HsdOpeningStock,
            HsdClosingStock  = dto.Summary.HsdClosingStock,
            MsIOpeningStock  = dto.Summary.MsIOpeningStock,
            MsIClosingStock  = dto.Summary.MsIClosingStock,
            MsIIOpeningStock = dto.Summary.MsIIOpeningStock,
            MsIIClosingStock = dto.Summary.MsIIClosingStock,
        };

        foreach (var n in dto.NozzleReadings)
        {
            entity.NozzleReadings.Add(new AgsNozzleReading
            {
                NozzleNumber     = n.NozzleNumber,
                FuelType         = n.FuelType,
                PumpNumber       = n.PumpNumber,
                OpeningReading   = n.OpeningReading,
                ClosingReading   = n.ClosingReading,
                SaleLitres       = n.SaleLitres,
                TestingDeduction = n.TestingDeduction,
                NetSaleLitres    = n.NetSaleLitres,
            });
        }

        foreach (var t in dto.TankStocks)
        {
            entity.TankStocks.Add(new AgsTankStock
            {
                TankNumber          = t.TankNumber,
                FuelType            = t.FuelType,
                OpeningDipMM        = t.OpeningDipMM,
                ClosingDipMM        = t.ClosingDipMM,
                OpeningStockLitres  = t.OpeningStockLitres,
                ClosingStockLitres  = t.ClosingStockLitres,
                FuelDispensedLitres = t.FuelDispensedLitres,
                ReceiptLitres       = t.ReceiptLitres,
            });
        }

        return entity;
    }
}
