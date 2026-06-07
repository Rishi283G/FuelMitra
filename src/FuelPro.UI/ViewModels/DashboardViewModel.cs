using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Linq;

namespace FuelPro.UI.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ShiftCalculationService _calcService;
    private readonly IShiftRepository _shiftRepository;
    private readonly IDsmEntryRepository _dsmEntryRepository;
    private readonly IDsmCalculationService _dsmCalculationService;
    private readonly ICreditorRepaymentRepository _repaymentRepo;
    private readonly ICreditorRepository _creditorRepo;

    [ObservableProperty] private ShiftSummaryDto? _morningShift;
    [ObservableProperty] private ShiftSummaryDto? _afternoonShift;
    [ObservableProperty] private ShiftSummaryDto? _nightShift;
    [ObservableProperty] private double _todayTotalSale;
    [ObservableProperty] private double _todayTotalLitres;
    [ObservableProperty] private double _todayTotalCash;
    [ObservableProperty] private double _todayTotalDigital;
    [ObservableProperty] private double _todayCollection;
    [ObservableProperty] private int _pendingMismatchCount;
    [ObservableProperty] private double _todayTotalMismatch;

    [ObservableProperty] private double _todayTotalPhonePe;
    [ObservableProperty] private double _todayTotalPhonePeCardMorning;
    [ObservableProperty] private double _todayTotalPhonePeCardNight;
    [ObservableProperty] private double _todayTotalCreditCard;
    [ObservableProperty] private double _todayTotalPetroCard;
    [ObservableProperty] private double _todayTotalBankCash;
    [ObservableProperty] private double _todayTotalCashInHand;
    [ObservableProperty] private double _todayTotalExpenses;
    public System.Collections.ObjectModel.ObservableCollection<DebitRegisterRowDto> TodayDebtorsList { get; } = new();
    [ObservableProperty] private double _totalCreditorsToday;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;

    // Creditor Repayment & Tracker tracking
    public System.Collections.ObjectModel.ObservableCollection<FuelPro.Core.Models.Creditor> Creditors { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<FuelPro.Core.Models.CreditorRepayment> Repayments { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<CreditorBalanceDto> OutstandingCreditors { get; } = new();
    [ObservableProperty] private string _newCreditorName = "";
    [ObservableProperty] private string _newRepaymentMode = "Cash";
    [ObservableProperty] private string _newChequeNumber = "";
    [ObservableProperty] private double _newRepaymentAmount;
    [ObservableProperty] private string _repaymentStatusMessage = "";

    public string[] PaymentModes { get; } = { "Cash", "Cheque", "PhonePe", "PineLab Card" };

    // ─── AGS Tank Stock & Shift Status ─────────────────────────────────────
    // Shift import status
    [ObservableProperty] private bool _agsShiftAImported;
    [ObservableProperty] private bool _agsShiftBImported;
    [ObservableProperty] private bool _agsShiftCImported;
    [ObservableProperty] private string _agsShiftATime = "Pending";
    [ObservableProperty] private string _agsShiftBTime = "Pending";
    [ObservableProperty] private string _agsShiftCTime = "Pending";
    [ObservableProperty] private int _agsShiftsImported;
    [ObservableProperty] private bool _agsAllShiftsImported;

    // Day totals from AGS
    [ObservableProperty] private double _agsDayHsd;
    [ObservableProperty] private double _agsDayMsI;
    [ObservableProperty] private double _agsDayMsII;
    [ObservableProperty] private double _agsDayTotal;

    // Tank stock
    [ObservableProperty] private double _hsdOpeningStock;
    [ObservableProperty] private double _hsdClosingStock;
    [ObservableProperty] private double _hsdDispensed;
    [ObservableProperty] private double _msIOpeningStock;
    [ObservableProperty] private double _msIClosingStock;
    [ObservableProperty] private double _msIDispensed;
    [ObservableProperty] private double _msIIOpeningStock;
    [ObservableProperty] private double _msIIClosingStock;
    [ObservableProperty] private double _msIIDispensed;

    // Tank level %
    [ObservableProperty] private double _hsdLevelPct;
    [ObservableProperty] private double _msILevelPct;
    [ObservableProperty] private double _msIILevelPct;

    // Per-shift breakdown
    [ObservableProperty] private double _agsShiftAHsd;
    [ObservableProperty] private double _agsShiftBHsd;
    [ObservableProperty] private double _agsShiftCHsd;
    [ObservableProperty] private double _agsShiftAMsI;
    [ObservableProperty] private double _agsShiftBMsI;
    [ObservableProperty] private double _agsShiftCMsI;
    [ObservableProperty] private double _agsShiftAMsII;
    [ObservableProperty] private double _agsShiftBMsII;
    [ObservableProperty] private double _agsShiftCMsII;
    [ObservableProperty] private double _agsShiftATotal;
    [ObservableProperty] private double _agsShiftBTotal;
    [ObservableProperty] private double _agsShiftCTotal;

    // Nozzle day summary
    public ObservableCollection<NozzleDaySummaryRow> NozzleDaySummaries { get; } = new();

    // AGS status label
    [ObservableProperty] private string _agsStatusLabel = "No AGS data";
    [ObservableProperty] private bool _hasAgsData;

    public DashboardViewModel()
    {
        _calcService = App.Services.GetRequiredService<ShiftCalculationService>();
        _shiftRepository = App.Services.GetRequiredService<IShiftRepository>();
        _dsmEntryRepository = App.Services.GetRequiredService<IDsmEntryRepository>();
        _dsmCalculationService = App.Services.GetRequiredService<IDsmCalculationService>();
        _repaymentRepo = App.Services.GetRequiredService<ICreditorRepaymentRepository>();
        _creditorRepo = App.Services.GetRequiredService<ICreditorRepository>();
        _ = LoadDataAsync();
        _ = LoadAgsDataAsync();
    }

    partial void OnSelectedDateChanged(DateTime value)
    {
        _ = LoadDataAsync();
        _ = LoadAgsDataAsync();
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var morningResult = await _calcService.GetShiftSummaryAsync(SelectedDate, "A");
            MorningShift = morningResult.Success ? morningResult.Data : new ShiftSummaryDto();

            var afternoonResult = await _calcService.GetShiftSummaryAsync(SelectedDate, "B");
            AfternoonShift = afternoonResult.Success ? afternoonResult.Data : new ShiftSummaryDto();

            var nightResult = await _calcService.GetShiftSummaryAsync(SelectedDate, "C");
            NightShift = nightResult.Success ? nightResult.Data : new ShiftSummaryDto();

            TodayTotalSale = 0;
            TodayTotalLitres = (MorningShift?.TotalHsdLitres ?? 0) + (MorningShift?.TotalMsILitres ?? 0) + (MorningShift?.TotalMsIILitres ?? 0)
                + (AfternoonShift?.TotalHsdLitres ?? 0) + (AfternoonShift?.TotalMsILitres ?? 0) + (AfternoonShift?.TotalMsIILitres ?? 0)
                + (NightShift?.TotalHsdLitres ?? 0) + (NightShift?.TotalMsILitres ?? 0) + (NightShift?.TotalMsIILitres ?? 0);
            TodayTotalCash = 0;
            TodayTotalDigital = 0;
            TodayCollection = 0;
            PendingMismatchCount = 0;
            TodayTotalMismatch = 0;
            TotalCreditorsToday = 0;
            double totalDsmShort = 0;
            
            TodayTotalPhonePe = 0;
            TodayTotalPhonePeCardMorning = 0;
            TodayTotalPhonePeCardNight = 0;
            TodayTotalCreditCard = 0;
            TodayTotalPetroCard = 0;
            TodayTotalBankCash = 0;
            TodayTotalCashInHand = 0;
            TodayTotalExpenses = 0;
            TodayDebtorsList.Clear();

            foreach (var shiftType in new[] { "A", "B", "C" })
            {
                var shiftResult = await _shiftRepository.GetShiftAsync(SelectedDate, shiftType);
                if (!shiftResult.Success || shiftResult.Data == null) continue;
                var entriesResult = await _dsmEntryRepository.GetEntriesForShiftAsync(shiftResult.Data.ShiftId);
                if (!entriesResult.Success || entriesResult.Data == null) continue;
                foreach (var entry in entriesResult.Data)
                {
                    var calc = _dsmCalculationService.Calculate(new DsmEntryDto
                    {
                        DSMEntryId = entry.DsmEntryId,
                        NozzleReadings = entry.NozzleReadings.Select(r => new NozzleReadingDto { Amount = (decimal)r.Amount }).ToList(),
                        PaymentCollection = new PaymentCollectionDto
                        {
                            PhonePe = (decimal)((entry.PaymentCollection?.PhonePe ?? 0) + (entry.PaymentCollection?.PhonePeCardMorning ?? 0) + (entry.PaymentCollection?.PhonePeCardNight ?? 0)),
                            CreditCard = (decimal)((entry.PaymentCollection?.CreditCard ?? 0) + (entry.PaymentCollection?.PetroCard ?? 0)),
                            CashDeposit = (decimal)(entry.CashDenominations.Where(x => x.CashType == "Cash1").Sum(x => x.TotalAmount) + entry.CashDenominations.Where(x => x.CashType == "Cash2").Sum(x => x.TotalAmount) + (entry.PaymentCollection?.CashDeposit ?? 0)),
                            PhysicalCash = 0  // Others is informational only, not included in TotalInDirect
                        },
                        DebitEntries = entry.DebitEntries.Select(d => new DebitEntryDto { Amount = (decimal)d.Amount }).ToList(),
                        TestingEntries = entry.TestingEntries.Select(t => new TestingEntryDto
                        {
                            FuelType = t.FuelType,
                            Amount = (decimal)t.Amount
                        }).ToList(),
                        Expenses = entry.Expenses.Select(e => new ExpenseDto { Amount = (decimal)e.Amount }).ToList()
                    });

                    double mismatch = (double)calc.Mismatch;
                    if (mismatch < -0.01) {
                        totalDsmShort += Math.Abs(mismatch);
                    } else if (mismatch > 0.01) {
                        PendingMismatchCount++; // Only count excesses as pending mismatches
                    }

                    TodayTotalSale += (double)calc.GrossSales;
                    TodayCollection += (double)calc.TotalCollection;
                    TotalCreditorsToday += (double)calc.TotalCreditors;
                    
                    TodayTotalPhonePe += (entry.PaymentCollection?.PhonePe ?? 0);
                    TodayTotalPhonePeCardMorning += (entry.PaymentCollection?.PhonePeCardMorning ?? 0);
                    TodayTotalPhonePeCardNight += (entry.PaymentCollection?.PhonePeCardNight ?? 0);
                    TodayTotalCreditCard += (entry.PaymentCollection?.CreditCard ?? 0);
                    TodayTotalPetroCard += (entry.PaymentCollection?.PetroCard ?? 0);
                    TodayTotalBankCash += entry.CashDenominations.Where(x => x.CashType == "Cash1").Sum(x => x.TotalAmount) + (entry.PaymentCollection?.CashDeposit ?? 0);
                    TodayTotalCashInHand += entry.CashDenominations.Where(x => x.CashType == "Cash2").Sum(x => x.TotalAmount);
                    TodayTotalExpenses += entry.Expenses.Sum(x => x.Amount);
                    
                    foreach (var d in entry.DebitEntries)
                    {
                        TodayDebtorsList.Add(new DebitRegisterRowDto
                        {
                            DsmName = entry.DsmName ?? "",
                            PumpId = entry.PumpId,
                            DebtorName = d.DebtorName,
                            Amount = (double)d.Amount
                        });
                    }
                }

                // Add shift-level expenses
                var shiftExpResult = await App.Services.GetRequiredService<IExpenseRepository>().GetByShiftIdAsync(shiftResult.Data.ShiftId);
                if (shiftExpResult.Success && shiftExpResult.Data != null)
                {
                    var shiftExpSum = shiftExpResult.Data.Sum(e => e.Amount);
                    TodayTotalExpenses += shiftExpSum;
                    TodayCollection += shiftExpSum;
                }

                // Add shift-level other cash
                var otherCashResult = await App.Services.GetRequiredService<IShiftOtherCashRepository>().GetByShiftAsync(SelectedDate, shiftType);
                if (otherCashResult.Success && otherCashResult.Data != null)
                {
                    TodayTotalSale += otherCashResult.Data.Sum(o => o.Amount);
                }
            }

            // Recompute TodayTotalMismatch ignoring DSM Shorts
            TodayTotalMismatch = (TodayCollection + totalDsmShort) - TodayTotalSale;
            
            // If the final mismatch is NOT balanced, we consider that a pending mismatch for the day
            if (Math.Abs(TodayTotalMismatch) > 0.01 && PendingMismatchCount == 0)
            {
                 // If there's a day-level mismatch but no entry-level excess, we just flag 1 for the UI.
                 PendingMismatchCount = 1;
            }

            var repaymentsResult = await _repaymentRepo.GetByDateAsync(SelectedDate);
            Repayments.Clear();
            if (repaymentsResult.Success && repaymentsResult.Data != null)
            {
                foreach (var r in repaymentsResult.Data) Repayments.Add(r);
            }

            // Feature 3: Creditor Balance Tracker (Outstanding this month)
            await LoadOutstandingCreditorsAsync();

            var creditorsResult = await _creditorRepo.GetAllActiveAsync();
            Creditors.Clear();
            if (creditorsResult.Success && creditorsResult.Data != null)
            {
                foreach (var c in creditorsResult.Data) Creditors.Add(c);
            }
        }
        finally { IsLoading = false; }
    }

    private async Task LoadOutstandingCreditorsAsync()
    {
        OutstandingCreditors.Clear();
        
        // 1. Get all un-reconciled DSM entries for the month
        var result = await _dsmEntryRepository.GetEntriesForMonthAsync(SelectedDate.Year, SelectedDate.Month);
        if (!result.Success || result.Data == null) return;

        // Group debits by DebtorName where IsReconciled == false
        var unreturnedDebits = result.Data
            .Where(e => !e.IsReconciled)
            .SelectMany(e => e.DebitEntries)
            .GroupBy(d => d.DebtorName.Trim())
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Amount), StringComparer.OrdinalIgnoreCase);

        // 2. Subtract repayments made this month
        var allRepaymentsThisMonth = await _repaymentRepo.GetByMonthAsync(SelectedDate.Year, SelectedDate.Month);
        if (allRepaymentsThisMonth.Success && allRepaymentsThisMonth.Data != null)
        {
            var repaymentsByName = allRepaymentsThisMonth.Data
                .GroupBy(r => r.CreditorName.Trim())
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount), StringComparer.OrdinalIgnoreCase);

            foreach (var rep in repaymentsByName)
            {
                if (unreturnedDebits.ContainsKey(rep.Key))
                {
                    unreturnedDebits[rep.Key] -= (double)rep.Value;
                }
            }
        }

        // 3. Populate ObservableCollection with positive balances
        foreach (var kvp in unreturnedDebits.Where(k => k.Value > 0).OrderBy(k => k.Key))
        {
            OutstandingCreditors.Add(new CreditorBalanceDto
            {
                CreditorName = kvp.Key,
                TotalDebitAmount = (double)kvp.Value
            });
        }
    }

    [RelayCommand]
    private async Task AddRepaymentAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCreditorName) || NewRepaymentAmount <= 0)
        {
            RepaymentStatusMessage = "❌ Name and Amount are required";
            return;
        }

        var repayment = new FuelPro.Core.Models.CreditorRepayment
        {
            CreditorName = NewCreditorName.Trim(),
            RepaymentDate = SelectedDate.Date,
            PaymentMode = NewRepaymentMode,
            ChequeNo = NewRepaymentMode == "Cheque" ? NewChequeNumber?.Trim() : null,
            Amount = NewRepaymentAmount
        };

        var result = await _repaymentRepo.AddAsync(repayment);
        if (result.Success)
        {
            RepaymentStatusMessage = "✅ Repayment logged";
            NewCreditorName = "";
            NewRepaymentAmount = 0;
            NewChequeNumber = "";
            NewRepaymentMode = "Cash";
            _ = LoadDataAsync();
        }
        else
        {
            RepaymentStatusMessage = $"❌ {result.Error}";
        }
    }

    [RelayCommand]
    private async Task DeleteRepaymentAsync(FuelPro.Core.Models.CreditorRepayment? repayment)
    {
        if (repayment == null) return;
        var result = await _repaymentRepo.DeleteAsync(repayment.CreditorRepaymentId);
        if (result.Success) _ = LoadDataAsync();
    }

    // ─── AGS Daily Summary Loading ──────────────────────────────────────────

    [RelayCommand]
    private async Task LoadAgsDataAsync()
    {
        try
        {
            var agsRepo = App.Services.GetRequiredService<IAgsImportRepository>();
            var agsAggSvc = App.Services.GetRequiredService<IAgsDailyAggregationService>();

            var shiftsResult = await agsRepo.GetShiftsForDateAsync(SelectedDate);
            if (!shiftsResult.Success || shiftsResult.Data == null || !shiftsResult.Data.Any())
            {
                HasAgsData = false;
                AgsStatusLabel = "No AGS data imported for this date";
                return;
            }

            var summaryResult = await agsRepo.GetDailySummaryAsync(SelectedDate);
            if (!summaryResult.Success || summaryResult.Data == null)
            {
                HasAgsData = false;
                AgsStatusLabel = "AGS summary not found";
                return;
            }

            var dto = agsAggSvc.ToDto(summaryResult.Data, shiftsResult.Data);

            // Shift status
            AgsShiftAImported = dto.ShiftAImported;
            AgsShiftBImported = dto.ShiftBImported;
            AgsShiftCImported = dto.ShiftCImported;
            AgsShiftsImported = dto.ShiftsImportedCount;
            AgsAllShiftsImported = dto.AllShiftsImported;

            AgsShiftATime = dto.ShiftAData != null ? dto.ShiftAData.ImportedAt.ToString("hh:mm tt") : "Pending";
            AgsShiftBTime = dto.ShiftBData != null ? dto.ShiftBData.ImportedAt.ToString("hh:mm tt") : "Pending";
            AgsShiftCTime = dto.ShiftCData != null ? dto.ShiftCData.ImportedAt.ToString("hh:mm tt") : "Pending";

            // Day totals
            AgsDayHsd   = dto.DayTotalHsdLitres;
            AgsDayMsI   = dto.DayTotalMsILitres;
            AgsDayMsII  = dto.DayTotalMsIILitres;
            AgsDayTotal = dto.DayGrandTotalLitres;

            // Tank stock
            HsdOpeningStock = dto.HsdDayOpeningStock;
            HsdClosingStock = dto.HsdDayClosingStock;
            HsdDispensed    = HsdOpeningStock - HsdClosingStock;
            MsIOpeningStock  = dto.MsIDayOpeningStock;
            MsIClosingStock  = dto.MsIDayClosingStock;
            MsIDispensed     = MsIOpeningStock - MsIClosingStock;
            MsIIOpeningStock = dto.MsIIDayOpeningStock;
            MsIIClosingStock = dto.MsIIDayClosingStock;
            MsIIDispensed    = MsIIOpeningStock - MsIIClosingStock;

            // Tank level % (closing/opening)
            HsdLevelPct  = HsdOpeningStock  > 0 ? HsdClosingStock  / HsdOpeningStock  * 100 : 0;
            MsILevelPct  = MsIOpeningStock  > 0 ? MsIClosingStock  / MsIOpeningStock  * 100 : 0;
            MsIILevelPct = MsIIOpeningStock > 0 ? MsIIClosingStock / MsIIOpeningStock * 100 : 0;

            // Per-shift breakdown
            AgsShiftAHsd  = dto.ShiftAData?.HsdLitres  ?? 0;
            AgsShiftAMsI  = dto.ShiftAData?.MsILitres  ?? 0;
            AgsShiftAMsII = dto.ShiftAData?.MsIILitres ?? 0;
            AgsShiftATotal = dto.ShiftAData?.TotalLitres ?? 0;
            AgsShiftBHsd  = dto.ShiftBData?.HsdLitres  ?? 0;
            AgsShiftBMsI  = dto.ShiftBData?.MsILitres  ?? 0;
            AgsShiftBMsII = dto.ShiftBData?.MsIILitres ?? 0;
            AgsShiftBTotal = dto.ShiftBData?.TotalLitres ?? 0;
            AgsShiftCHsd  = dto.ShiftCData?.HsdLitres  ?? 0;
            AgsShiftCMsI  = dto.ShiftCData?.MsILitres  ?? 0;
            AgsShiftCMsII = dto.ShiftCData?.MsIILitres ?? 0;
            AgsShiftCTotal = dto.ShiftCData?.TotalLitres ?? 0;

            // Nozzle summary (all 28)
            NozzleDaySummaries.Clear();

            for (int n = 1; n <= 28; n++)
            {
                double shiftA = 0, shiftB = 0, shiftC = 0;
                foreach (var shift in shiftsResult.Data)
                {
                    var nozzleReading = shift.NozzleReadings.FirstOrDefault(r => r.NozzleNumber == n);
                    if (nozzleReading == null) continue;
                    if (shift.ShiftType == "A") shiftA = nozzleReading.NetSaleLitres;
                    else if (shift.ShiftType == "B") shiftB = nozzleReading.NetSaleLitres;
                    else if (shift.ShiftType == "C") shiftC = nozzleReading.NetSaleLitres;
                }

                // Find pump number from PumpConfiguration
                int pumpNumber = PumpConfiguration.PumpNozzleMapping
                    .FirstOrDefault(kv => kv.Value.Contains(n)).Key;

                NozzleDaySummaries.Add(new NozzleDaySummaryRow
                {
                    NozzleNumber = n,
                    FuelType     = PumpConfiguration.GetFuelTypeDisplayName(n),
                    PumpNumber   = pumpNumber,
                    ShiftALitres = shiftA,
                    ShiftBLitres = shiftB,
                    ShiftCLitres = shiftC,
                    DayTotal     = shiftA + shiftB + shiftC,
                });
            }

            AgsStatusLabel = dto.AllShiftsImported
                ? $"{SelectedDate:dd-MMM-yyyy} — All 3 shifts imported"
                : $"{SelectedDate:dd-MMM-yyyy} — {dto.ShiftsImportedCount}/3 shifts imported (partial)";
            HasAgsData = true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "AGS dashboard load failed");
            HasAgsData = false;
            AgsStatusLabel = "AGS data unavailable";
        }
    }
}

// ─── Supporting Row Model for Nozzle Table ─────────────────────────────────
public class NozzleDaySummaryRow
{
    public int    NozzleNumber { get; set; }
    public string FuelType     { get; set; } = string.Empty;
    public int    PumpNumber   { get; set; }
    public double ShiftALitres { get; set; }
    public double ShiftBLitres { get; set; }
    public double ShiftCLitres { get; set; }
    public double DayTotal     { get; set; }
    public string PumpDisplay  => $"P{PumpNumber}";
}

