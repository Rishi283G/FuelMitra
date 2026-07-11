using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Models.AGS;
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
    [ObservableProperty] private DateTime _startDate = DateTime.Today;
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private string _selectedPreset = "Today";

    public string[] Presets { get; } = { "Today", "Yesterday", "This Week", "This Month", "Last 7 Days", "Last 30 Days", "Custom" };

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

    private bool _isUpdatingPreset;

    partial void OnSelectedDateChanged(DateTime value)
    {
        if (!_isUpdatingPreset)
        {
            _isUpdatingPreset = true;
            StartDate = value;
            EndDate = value;
            _isUpdatingPreset = false;
            _ = LoadDataAsync();
            _ = LoadAgsDataAsync();
        }
    }

    partial void OnSelectedPresetChanged(string value)
    {
        if (value == "Custom") return;

        _isUpdatingPreset = true;
        var today = DateTime.Today;
        DateTime newStart = today;
        DateTime newEnd = today;
        switch (value)
        {
            case "Today":
                newStart = today;
                newEnd = today;
                break;
            case "Yesterday":
                newStart = today.AddDays(-1);
                newEnd = today.AddDays(-1);
                break;
            case "This Week":
                int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                newStart = today.AddDays(-1 * diff).Date;
                newEnd = today;
                break;
            case "This Month":
                newStart = new DateTime(today.Year, today.Month, 1);
                newEnd = today;
                break;
            case "Last 7 Days":
                newStart = today.AddDays(-6);
                newEnd = today;
                break;
            case "Last 30 Days":
                newStart = today.AddDays(-29);
                newEnd = today;
                break;
        }

        StartDate = newStart;
        EndDate = newEnd;
        SelectedDate = newEnd;
        _isUpdatingPreset = false;

        _ = LoadDataAsync();
        _ = LoadAgsDataAsync();
    }

    partial void OnStartDateChanged(DateTime value)
    {
        if (!_isUpdatingPreset)
        {
            SelectedPreset = "Custom";
            _ = LoadDataAsync();
            _ = LoadAgsDataAsync();
        }
    }

    partial void OnEndDateChanged(DateTime value)
    {
        SelectedDate = value;
        if (!_isUpdatingPreset)
        {
            SelectedPreset = "Custom";
            _ = LoadDataAsync();
            _ = LoadAgsDataAsync();
        }
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var entriesResult = await _dsmEntryRepository.GetEntriesForDateRangeAsync(StartDate, EndDate);
            var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

            var shiftsResult = await _shiftRepository.GetShiftsByDateRangeAsync(StartDate, EndDate);
            var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
            var shiftIds = shifts.Select(s => s.ShiftId).ToList();

            var otherCashResult = await App.Services.GetRequiredService<IShiftOtherCashRepository>().GetByDateRangeAsync(StartDate, EndDate);
            var otherCashList = otherCashResult.Success && otherCashResult.Data != null ? otherCashResult.Data : new List<ShiftOtherCash>();

            var shiftExpensesResult = await App.Services.GetRequiredService<IExpenseRepository>().GetExpensesByShiftIdsAsync(shiftIds);
            var shiftExpenses = shiftExpensesResult.Success && shiftExpensesResult.Data != null ? shiftExpensesResult.Data : new List<Expense>();

            MorningShift = AggregateShiftSummary(shifts, entries, otherCashList, shiftExpenses, "A");
            AfternoonShift = AggregateShiftSummary(shifts, entries, otherCashList, shiftExpenses, "B");
            NightShift = null;

            TodayTotalSale = 0;
            TodayTotalLitres = (MorningShift?.TotalHsdLitres ?? 0) + (MorningShift?.TotalMsILitres ?? 0) + (MorningShift?.TotalMsIILitres ?? 0)
                + (AfternoonShift?.TotalHsdLitres ?? 0) + (AfternoonShift?.TotalMsILitres ?? 0) + (AfternoonShift?.TotalMsIILitres ?? 0);
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

            var entryCalculations = new System.Collections.Generic.List<(int ShiftId, string DsmName, int PumpId, int? ReconciledToPumpId, double Mismatch)>();

            foreach (var entry in entries)
            {
                var cash1 = entry.CashDenominations.Where(x => x.CashType == "Cash1").Sum(x => x.TotalAmount);
                var cash2 = entry.CashDenominations.Where(x => x.CashType == "Cash2").Sum(x => x.TotalAmount);

                var calc = _dsmCalculationService.Calculate(new DsmEntryDto
                {
                    DSMEntryId = entry.DsmEntryId,
                    NozzleReadings = entry.NozzleReadings.Select(r => new NozzleReadingDto { Amount = (decimal)r.Amount }).ToList(),
                    PaymentCollection = new PaymentCollectionDto
                    {
                        PhonePe = (decimal)((entry.PaymentCollection?.PhonePeMorning ?? 0)
                                            + (entry.PaymentCollection?.PhonePeDay ?? 0)
                                            + (entry.PaymentCollection?.PhonePeNight ?? 0)
                                            + (entry.PaymentCollection?.PhonePeCardMorning ?? 0)
                                            + (entry.PaymentCollection?.PhonePeCardDay ?? 0)
                                            + (entry.PaymentCollection?.PhonePeCardNight ?? 0)),
                        CreditCard = (decimal)((entry.PaymentCollection?.CreditCardMorning ?? 0)
                                               + (entry.PaymentCollection?.CreditCardDay ?? 0)
                                               + (entry.PaymentCollection?.CreditCardNight ?? 0)),
                        PetroCard = (decimal)((entry.PaymentCollection?.PetroCardMorning ?? 0)
                                              + (entry.PaymentCollection?.PetroCardDay ?? 0)
                                              + (entry.PaymentCollection?.PetroCardNight ?? 0)),
                        CashDeposit = (decimal)(cash1 > 0 ? cash1 : (entry.PaymentCollection?.CashDeposit ?? 0)),
                        PhysicalCash = (decimal)cash2
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
                entryCalculations.Add((entry.ShiftId, entry.DsmName ?? "", entry.PumpId, entry.ReconciledToPumpId, mismatch));

                TodayTotalSale += (double)calc.GrossSales;
                TodayCollection += (double)calc.TotalCollection;
                TotalCreditorsToday += (double)calc.TotalCreditors;
                
                TodayTotalPhonePe += (entry.PaymentCollection?.PhonePeMorning ?? 0)
                                     + (entry.PaymentCollection?.PhonePeDay ?? 0)
                                     + (entry.PaymentCollection?.PhonePeNight ?? 0);
                TodayTotalPhonePeCardMorning += (entry.PaymentCollection?.PhonePeCardMorning ?? 0)
                                                + (entry.PaymentCollection?.PhonePeCardDay ?? 0);
                TodayTotalPhonePeCardNight += (entry.PaymentCollection?.PhonePeCardNight ?? 0);
                TodayTotalCreditCard += (entry.PaymentCollection?.CreditCardMorning ?? 0)
                                         + (entry.PaymentCollection?.CreditCardDay ?? 0)
                                         + (entry.PaymentCollection?.CreditCardNight ?? 0);
                TodayTotalPetroCard += (entry.PaymentCollection?.PetroCardMorning ?? 0)
                                        + (entry.PaymentCollection?.PetroCardDay ?? 0)
                                        + (entry.PaymentCollection?.PetroCardNight ?? 0);
                TodayTotalBankCash += cash1 > 0 ? cash1 : (entry.PaymentCollection?.CashDeposit ?? 0);
                TodayTotalCashInHand += cash2;
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
            if (shiftExpenses.Any())
            {
                var shiftExpSum = shiftExpenses.Sum(e => e.Amount);
                TodayTotalExpenses += shiftExpSum;
                TodayCollection += shiftExpSum;
            }

            // Add shift-level other cash
            if (otherCashList.Any())
            {
                TodayTotalSale += otherCashList.Sum(o => o.Amount);
            }

            // Group by shift, name, and connected/reconciled pump (matching DayTotal calculation)
            var mismatchGroups = entryCalculations.GroupBy(e => new { e.ShiftId, e.DsmName, GroupPumpId = e.ReconciledToPumpId ?? e.PumpId });
            foreach (var g in mismatchGroups)
            {
                var sumMismatch = g.Sum(e => e.Mismatch);
                if (sumMismatch < -0.01)
                {
                    totalDsmShort += Math.Abs(sumMismatch);
                }
                else if (sumMismatch > 0.01)
                {
                    PendingMismatchCount++;
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

            var repaymentsResult = await _repaymentRepo.GetByDateRangeAsync(StartDate, EndDate);
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

    private ShiftSummaryDto AggregateShiftSummary(
        List<Shift> shifts,
        List<DsmEntry> entries,
        List<ShiftOtherCash> otherCashList,
        List<Expense> shiftExpenses,
        string shiftType)
    {
        var shiftTypeShifts = shifts.Where(s => s.ShiftType == shiftType).ToList();
        var shiftIds = shiftTypeShifts.Select(s => s.ShiftId).ToHashSet();
        
        var shiftEntries = entries.Where(e => shiftIds.Contains(e.ShiftId)).ToList();
        var shiftOtherCash = otherCashList.Where(o => o.ShiftNumber == shiftType).ToList();
        var typeShiftExpenses = shiftExpenses.Where(e => e.ShiftId != null && shiftIds.Contains(e.ShiftId.Value)).ToList();

        var summary = new ShiftSummaryDto
        {
            ShiftDate = EndDate,
            ShiftType = shiftType,
            TotalDsmEntries = shiftEntries.Count,
            IsLocked = shiftTypeShifts.Any() && shiftTypeShifts.All(s => s.IsLocked)
        };

        double totalFuelSale = 0;
        double totalCash = 0;
        double totalDigital = 0;
        double totalDebit = 0;
        double totalExpenses = typeShiftExpenses.Sum(e => e.Amount);

        foreach (var entry in shiftEntries)
        {
            var cash1 = entry.CashDenominations.Where(c => c.CashType == "Cash1").Sum(c => c.TotalAmount);
            var cash2 = entry.CashDenominations.Where(c => c.CashType == "Cash2").Sum(c => c.TotalAmount);
            var calc = _dsmCalculationService.Calculate(new DsmEntryDto
            {
                DSMEntryId = entry.DsmEntryId,
                NozzleReadings = entry.NozzleReadings.Select(n => new NozzleReadingDto { Amount = (decimal)n.Amount }).ToList(),
                PaymentCollection = new PaymentCollectionDto
                {
                    PhonePe = (decimal)((entry.PaymentCollection?.PhonePeMorning ?? 0)
                                        + (entry.PaymentCollection?.PhonePeDay ?? 0)
                                        + (entry.PaymentCollection?.PhonePeNight ?? 0)
                                        + (entry.PaymentCollection?.PhonePeCardMorning ?? 0)
                                        + (entry.PaymentCollection?.PhonePeCardDay ?? 0)
                                        + (entry.PaymentCollection?.PhonePeCardNight ?? 0)),
                    CreditCard = (decimal)((entry.PaymentCollection?.CreditCardMorning ?? 0)
                                           + (entry.PaymentCollection?.CreditCardDay ?? 0)
                                           + (entry.PaymentCollection?.CreditCardNight ?? 0)),
                    PetroCard = (decimal)((entry.PaymentCollection?.PetroCardMorning ?? 0)
                                          + (entry.PaymentCollection?.PetroCardDay ?? 0)
                                          + (entry.PaymentCollection?.PetroCardNight ?? 0)),
                    CashDeposit = (decimal)(cash1 + (entry.PaymentCollection?.CashDeposit ?? 0)),
                    PhysicalCash = (decimal)cash2
                },
                DebitEntries = entry.DebitEntries.Select(d => new DebitEntryDto { Amount = (decimal)d.Amount }).ToList(),
                TestingEntries = entry.TestingEntries.Select(t => new TestingEntryDto
                {
                    FuelType = t.FuelType,
                    Amount = (decimal)t.Amount
                }).ToList()
            });

            totalFuelSale += (double)calc.GrossSales;
            totalDebit += (double)calc.TotalCreditors;
            totalExpenses += entry.Expenses.Sum(e => e.Amount);
            
            var phonePe = (double)((entry.PaymentCollection?.PhonePeMorning ?? 0)
                                   + (entry.PaymentCollection?.PhonePeDay ?? 0)
                                   + (entry.PaymentCollection?.PhonePeNight ?? 0)
                                   + (entry.PaymentCollection?.PhonePeCardMorning ?? 0)
                                   + (entry.PaymentCollection?.PhonePeCardDay ?? 0)
                                   + (entry.PaymentCollection?.PhonePeCardNight ?? 0));
            var petroCard = (double)((entry.PaymentCollection?.PetroCardMorning ?? 0)
                                     + (entry.PaymentCollection?.PetroCardDay ?? 0)
                                     + (entry.PaymentCollection?.PetroCardNight ?? 0));
            var creditCard = (double)((entry.PaymentCollection?.CreditCardMorning ?? 0)
                                      + (entry.PaymentCollection?.CreditCardDay ?? 0)
                                      + (entry.PaymentCollection?.CreditCardNight ?? 0));
            totalDigital += phonePe + petroCard + creditCard;
            
            totalCash += cash1 + cash2;
        }

        totalFuelSale += shiftOtherCash.Sum(o => o.Amount);

        summary.TotalFuelSale = totalFuelSale;
        summary.TotalCash = totalCash;
        summary.TotalDigitalPayments = totalDigital;
        summary.TotalDebit = totalDebit;
        summary.TotalExpenses = totalExpenses;

        summary.TotalHsdLitres = shiftEntries.SelectMany(e => e.NozzleReadings.Select(r => new { e.PumpId, Reading = r }))
            .Where(x => PumpConfiguration.GetFuelTypeDisplayName(x.PumpId, x.Reading.NozzleNumber, EndDate) == "HSD")
            .Sum(x => x.Reading.SaleLitres);
        summary.TotalMsILitres = shiftEntries.SelectMany(e => e.NozzleReadings.Select(r => new { e.PumpId, Reading = r }))
            .Where(x => PumpConfiguration.GetFuelTypeDisplayName(x.PumpId, x.Reading.NozzleNumber, EndDate) == "MS-I")
            .Sum(x => x.Reading.SaleLitres);
        summary.TotalMsIILitres = shiftEntries.SelectMany(e => e.NozzleReadings.Select(r => new { e.PumpId, Reading = r }))
            .Where(x => PumpConfiguration.GetFuelTypeDisplayName(x.PumpId, x.Reading.NozzleNumber, EndDate) == "MS-II")
            .Sum(x => x.Reading.SaleLitres);

        return summary;
    }

    private async Task LoadOutstandingCreditorsAsync()
    {
        OutstandingCreditors.Clear();
        
        var result = await _dsmEntryRepository.GetEntriesForMonthAsync(EndDate.Year, EndDate.Month);
        if (!result.Success || result.Data == null) return;

        var unreturnedDebits = result.Data
            .Where(e => !e.IsReconciled)
            .SelectMany(e => e.DebitEntries)
            .GroupBy(d => d.DebtorName.Trim())
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Amount), StringComparer.OrdinalIgnoreCase);

        var allRepaymentsThisMonth = await _repaymentRepo.GetByMonthAsync(EndDate.Year, EndDate.Month);
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

        foreach (var kvp in unreturnedDebits.Where(k => k.Value > 0).OrderBy(k => k.Key))
        {
            OutstandingCreditors.Add(new CreditorBalanceDto
            {
                DebtorName = kvp.Key,
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

            var allShifts = new List<AgsShiftImport>();
            var allSummaries = new List<AgsDailySummary>();
            var totalDays = (EndDate.Date - StartDate.Date).Days + 1;

            for (var dt = StartDate.Date; dt <= EndDate.Date; dt = dt.AddDays(1))
            {
                var shiftsRes = await agsRepo.GetShiftsForDateAsync(dt);
                if (shiftsRes.Success && shiftsRes.Data != null)
                {
                    allShifts.AddRange(shiftsRes.Data.Where(s => s.IsActive));
                }
                var summaryRes = await agsRepo.GetDailySummaryAsync(dt);
                if (summaryRes.Success && summaryRes.Data != null)
                {
                    allSummaries.Add(summaryRes.Data);
                }
            }

            if (!allShifts.Any())
            {
                HasAgsData = false;
                AgsStatusLabel = "No AGS data imported for this date range";
                return;
            }

            // Shift status
            AgsShiftAImported = allShifts.Any(s => s.ShiftType == "A");
            AgsShiftBImported = allShifts.Any(s => s.ShiftType == "B");
            AgsShiftCImported = allShifts.Any(s => s.ShiftType == "C");
            AgsShiftsImported = allShifts.Count;
            AgsAllShiftsImported = allShifts.Count == totalDays * 3;

            if (StartDate.Date == EndDate.Date)
            {
                var shiftA = allShifts.FirstOrDefault(s => s.ShiftType == "A");
                var shiftB = allShifts.FirstOrDefault(s => s.ShiftType == "B");
                var shiftC = allShifts.FirstOrDefault(s => s.ShiftType == "C");
                AgsShiftATime = shiftA != null ? shiftA.ImportedAt.ToString("hh:mm tt") : "Pending";
                AgsShiftBTime = shiftB != null ? shiftB.ImportedAt.ToString("hh:mm tt") : "Pending";
                AgsShiftCTime = shiftC != null ? shiftC.ImportedAt.ToString("hh:mm tt") : "Pending";
            }
            else
            {
                var countA = allShifts.Count(s => s.ShiftType == "A");
                var countB = allShifts.Count(s => s.ShiftType == "B");
                var countC = allShifts.Count(s => s.ShiftType == "C");
                AgsShiftATime = $"{countA}/{totalDays} Days";
                AgsShiftBTime = $"{countB}/{totalDays} Days";
                AgsShiftCTime = $"{countC}/{totalDays} Days";
            }

            // Day totals
            AgsDayHsd = 0;
            AgsDayMsI = 0;
            AgsDayMsII = 0;
            AgsDayTotal = 0;

            foreach (var s in allShifts)
            {
                double hsd = 0, msI = 0, msII = 0;
                if (s.NozzleReadings != null && s.NozzleReadings.Any())
                {
                    hsd  = s.NozzleReadings.Where(r => PumpConfiguration.GetFuelTypeDisplayName(r.PumpNumber, r.NozzleNumber, s.ImportDate) == "HSD"  ).Sum(r => r.NetSaleLitres);
                    msI  = s.NozzleReadings.Where(r => PumpConfiguration.GetFuelTypeDisplayName(r.PumpNumber, r.NozzleNumber, s.ImportDate) == "MS-I" ).Sum(r => r.NetSaleLitres);
                    msII = s.NozzleReadings.Where(r => PumpConfiguration.GetFuelTypeDisplayName(r.PumpNumber, r.NozzleNumber, s.ImportDate) == "MS-II").Sum(r => r.NetSaleLitres);
                }
                else
                {
                    hsd  = s.TotalHsdLitres;
                    msI  = s.TotalMsILitres;
                    msII = s.TotalMsIILitres;
                }
                AgsDayHsd += hsd;
                AgsDayMsI += msI;
                AgsDayMsII += msII;
                AgsDayTotal += (hsd + msI + msII);
            }

            // Tank stock (Opening from earliest shift, closing from latest shift)
            var sortedShifts = allShifts.OrderBy(s => s.ImportDate).ThenBy(s => s.ShiftType == "A").ToList();
            var firstShift = sortedShifts.FirstOrDefault();
            var lastShift = sortedShifts.LastOrDefault();

            HsdOpeningStock = firstShift?.HsdOpeningStock ?? 0;
            HsdClosingStock = lastShift?.HsdClosingStock ?? 0;
            HsdDispensed    = AgsDayHsd;

            MsIOpeningStock  = firstShift?.MsIOpeningStock ?? 0;
            MsIClosingStock  = lastShift?.MsIClosingStock ?? 0;
            MsIDispensed     = AgsDayMsI;

            MsIIOpeningStock = firstShift?.MsIIOpeningStock ?? 0;
            MsIIClosingStock = lastShift?.MsIIClosingStock ?? 0;
            MsIIDispensed    = AgsDayMsII;

            // Tank level % (closing/opening)
            HsdLevelPct  = HsdOpeningStock  > 0 ? HsdClosingStock  / HsdOpeningStock  * 100 : 0;
            MsILevelPct  = MsIOpeningStock  > 0 ? MsIClosingStock  / MsIOpeningStock  * 100 : 0;
            MsIILevelPct = MsIIOpeningStock > 0 ? MsIIClosingStock / MsIIOpeningStock * 100 : 0;

            // Per-shift breakdown
            AgsShiftAHsd  = 0; AgsShiftAMsI  = 0; AgsShiftAMsII = 0; AgsShiftATotal = 0;
            AgsShiftBHsd  = 0; AgsShiftBMsI  = 0; AgsShiftBMsII = 0; AgsShiftBTotal = 0;
            AgsShiftCHsd  = 0; AgsShiftCMsI  = 0; AgsShiftCMsII = 0; AgsShiftCTotal = 0;

            foreach (var s in allShifts)
            {
                double hsd = 0, msI = 0, msII = 0;
                if (s.NozzleReadings != null && s.NozzleReadings.Any())
                {
                    hsd  = s.NozzleReadings.Where(r => PumpConfiguration.GetFuelTypeDisplayName(r.PumpNumber, r.NozzleNumber, s.ImportDate) == "HSD"  ).Sum(r => r.NetSaleLitres);
                    msI  = s.NozzleReadings.Where(r => PumpConfiguration.GetFuelTypeDisplayName(r.PumpNumber, r.NozzleNumber, s.ImportDate) == "MS-I" ).Sum(r => r.NetSaleLitres);
                    msII = s.NozzleReadings.Where(r => PumpConfiguration.GetFuelTypeDisplayName(r.PumpNumber, r.NozzleNumber, s.ImportDate) == "MS-II").Sum(r => r.NetSaleLitres);
                }
                else
                {
                    hsd  = s.TotalHsdLitres;
                    msI  = s.TotalMsILitres;
                    msII = s.TotalMsIILitres;
                }

                if (s.ShiftType == "A")
                {
                    AgsShiftAHsd += hsd; AgsShiftAMsI += msI; AgsShiftAMsII += msII; AgsShiftATotal += (hsd + msI + msII);
                }
                else if (s.ShiftType == "B")
                {
                    AgsShiftBHsd += hsd; AgsShiftBMsI += msI; AgsShiftBMsII += msII; AgsShiftBTotal += (hsd + msI + msII);
                }
            }

            // Nozzle summary
            NozzleDaySummaries.Clear();

            foreach (var nozzleInfo in PumpConfiguration.AllNozzles)
            {
                int n = nozzleInfo.NozzleNumber;
                int p = nozzleInfo.PumpId;
                double shiftA = 0, shiftB = 0;
                foreach (var shift in allShifts)
                {
                    var nozzleReading = shift.NozzleReadings.FirstOrDefault(r => r.NozzleNumber == n && r.PumpNumber == p);
                    if (nozzleReading == null) continue;
                    if (shift.ShiftType == "A") shiftA += nozzleReading.NetSaleLitres;
                    else if (shift.ShiftType == "B") shiftB += nozzleReading.NetSaleLitres;
                }

                NozzleDaySummaries.Add(new NozzleDaySummaryRow
                {
                    NozzleNumber = n,
                    FuelType     = PumpConfiguration.GetFuelTypeDisplayName(p, n, EndDate),
                    PumpNumber   = p,
                    ShiftALitres = shiftA,
                    ShiftBLitres = shiftB,
                    ShiftCLitres = 0,
                    DayTotal     = shiftA + shiftB,
                });
            }

            if (StartDate.Date == EndDate.Date)
            {
                AgsStatusLabel = allShifts.Count == 2
                    ? $"{StartDate:dd-MMM-yyyy} — All 2 shifts imported"
                    : $"{StartDate:dd-MMM-yyyy} — {allShifts.Count}/2 shifts imported (partial)";
            }
            else
            {
                var totalExpected = totalDays * 2;
                AgsStatusLabel = allShifts.Count == totalExpected
                    ? $"{StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy} — All {allShifts.Count} shifts imported"
                    : $"{StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy} — {allShifts.Count}/{totalExpected} shifts imported (partial)";
            }
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

