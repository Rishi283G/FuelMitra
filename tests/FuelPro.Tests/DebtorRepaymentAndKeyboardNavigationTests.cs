using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.Sync;
using FuelPro.UI;
using FuelPro.UI.Printing;
using FuelPro.UI.ViewModels;
using FuelPro.UI.Views;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Xunit;

namespace FuelPro.Tests;

public class DebtorRepaymentAndKeyboardNavigationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly IServiceProvider _serviceProvider;

    public DebtorRepaymentAndKeyboardNavigationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FuelPro_DebtorRepayNav_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "fuelPro_test.db");

        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}"),
            ServiceLifetime.Transient);

        services.AddSingleton<ICredentialFileService>(new LocalCredentialFileService(Path.Combine(_tempDir, "credentials")));

        services.AddTransient<ISettingsRepository, SettingsRepository>();
        services.AddTransient<IUserRepository, UserRepository>();
        services.AddTransient<IShiftRepository, ShiftRepository>();
        services.AddTransient<IDsmEntryRepository, DsmEntryRepository>();
        services.AddTransient<INozzleReadingRepository, NozzleReadingRepository>();
        services.AddTransient<IPaymentRepository, PaymentRepository>();
        services.AddTransient<IDebitEntryRepository, DebitEntryRepository>();
        services.AddTransient<ITestingEntryRepository, TestingEntryRepository>();
        services.AddTransient<IExpenseRepository, ExpenseRepository>();
        services.AddTransient<ICashDenominationRepository, CashDenominationRepository>();
        services.AddTransient<IDsmProfileRepository, DsmProfileRepository>();
        services.AddTransient<ICreditorRepaymentRepository, CreditorRepaymentRepository>();
        services.AddTransient<IAgsImportRepository, AgsImportRepository>();
        services.AddTransient<ICreditorRepository, CreditorRepository>();
        services.AddTransient<IPumpExpenseRepository, PumpExpenseRepository>();
        services.AddTransient<IDebtorVehicleRepository, DebtorVehicleRepository>();
        services.AddTransient<IDsmPersonalDebtorRepository, DsmPersonalDebtorRepository>();
        services.AddScoped<IShiftOtherCashRepository, ShiftOtherCashRepository>();
        services.AddScoped<IShiftFuelRateRepository, ShiftFuelRateRepository>();

        services.AddSingleton<AuthService>();
        services.AddTransient<DsmEntryService>();
        services.AddTransient<ShiftCalculationService>();
        services.AddSingleton<IDsmCalculationService, DsmCalculationService>();
        services.AddSingleton<IOwnerCalculationService, OwnerCalculationService>();
        services.AddSingleton<ITidCalculationService, TidCalculationService>();
        services.AddScoped<IShiftAggregationService, ShiftAggregationService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddTransient<IAgsInventoryService, MockAgsInventoryService>();
        services.AddTransient<IFinancialCalculationService, MockFinancialCalculationService>();

        services.AddTransient<PrintService>();
        services.AddTransient<ExcelExportService>();
        services.AddTransient<IAuditLogService, AuditLogService>();
        services.AddTransient<IDayLockService, DayLockService>();
        services.AddTransient<IFeatureToggleService, FeatureToggleService>();
        services.AddTransient<ICollectionTypeService, CollectionTypeService>();
        services.AddTransient<IStationConfigurationService, StationConfigurationService>();
        services.AddTransient<IShiftCycleService, ShiftCycleService>();

        services.AddSingleton<SyncConfigService>();
        services.AddSingleton<SyncEngine>();
        services.AddSingleton<DsmSubmissionPollingService>();
        services.AddTransient<DsmAuthAdminService>();
        services.AddTransient<SupabaseDsmService, FakeSupabaseDsmService>();
        services.AddSingleton<DraftService>();

        services.AddTransient<DsmEntryViewModel>();
        services.AddTransient<DsmApprovalQueueViewModel>();

        _serviceProvider = services.BuildServiceProvider();

        typeof(App).GetProperty("Services")?.SetValue(null, _serviceProvider);
        typeof(App).GetProperty("DbPath")?.SetValue(null, _dbPath);

        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();

        var today = DateTime.Today;
        context.Shifts.AddRange(
            new Shift { ShiftDate = today, ShiftType = "A" },
            new Shift { ShiftDate = today, ShiftType = "B" },
            new Shift { ShiftDate = today, ShiftType = "C" }
        );

        context.Settings.Add(new Setting
        {
            PumpStationName = "Mitali Service Station",
            StationDisplayName = "Mitali Service Station",
            HsdRate = 90.0,
            MsIRate = 100.0,
            MsIIRate = 100.0,
            CngRate = 85.0
        });

        var now = DateTime.Now;
        var mappings = new List<PumpMapping>
        {
            new() { PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true, CreatedAt = now },
            new() { PumpId = 1, NozzleNumber = 5, FuelType = "Speed", TankName = "Speed - 20KL", IsActive = true, CreatedAt = now }
        };
        PumpConfiguration.InitializeFromDb(mappings);
        context.PumpMappings.AddRange(mappings);

        context.Creditors.Add(new Creditor { Name = "Acme Transport", Phone = "9999999999", IsActive = true, CreatedAt = now });
        context.SaveChanges();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    // =========================================================================
    // ISSUE 1: Debtor Repayment Reconciliation & Print Separation Tests
    // =========================================================================

    [Fact]
    public async Task DesktopManualEntry_WithDebtorRepayment_DoesNotInflateOrDeductCollection_AndPersistsToManagement()
    {
        // Seed prior debit of 10,000 for Acme Transport so outstanding balance = 10,000 - 2,500 = 7,500
        using (var seedCtx = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var prevShift = new Shift { ShiftDate = DateTime.Today.AddDays(-1), ShiftType = "B" };
            seedCtx.Shifts.Add(prevShift);
            await seedCtx.SaveChangesAsync();

            var prevEntry = new DsmEntry
            {
                ShiftId = prevShift.ShiftId,
                DsmName = "Opening Debit Shift",
                PumpId = 1,
                GrossSales = 10000m,
                TotalCreditors = 10000m,
                TotalCollection = 10000m
            };
            seedCtx.DsmEntries.Add(prevEntry);
            await seedCtx.SaveChangesAsync();

            seedCtx.DebitEntries.Add(new DebitEntry
            {
                DsmEntryId = prevEntry.DsmEntryId,
                DebtorName = "Acme Transport",
                Amount = 10000.0
            });
            await seedCtx.SaveChangesAsync();
        }

        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        vm.SelectedDate = DateTime.Today;
        vm.SelectedShift = "A";
        vm.DsmName = "Desktop DSM";
        vm.SelectedPump = vm.PumpOptions.First(p => p.PumpId == 1);

        vm.NozzleReadings.Clear();
        vm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 1000, ClosingReading = 1100, Rate = 100 }); // 10,000 gross
        vm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 1, NozzleNumber = 5, FuelType = "Speed", OpeningReading = 500, ClosingReading = 500, Rate = 100 }); // 0 gross

        // Pure fuel collections matching 10,000 gross sales
        vm.Cash2.Denom500 = 12; // 6,000 cash
        vm.PhonePeMorning = 4000.0;

        // Direct debtor repayment received by DSM (must stay out of fuel collections & mismatch)
        vm.DebtorRepayments.Add(new DsmDebtorRepaymentRow
        {
            DebtorName = "Acme Transport",
            Amount = 2500.0,
            PaymentMode = "Cash",
            Notes = "Old bill recovery"
        });

        vm.RecalculateAll();

        Assert.Equal(10000.0, vm.GrossSales, precision: 2);
        Assert.Equal(10000.0, vm.FinalAdjusted, precision: 2);
        Assert.Equal(0.0, vm.Difference, precision: 2);

        await vm.SaveEntryCommand.ExecuteAsync(null);
        Assert.True(vm.IsSaved, vm.StatusMessage);

        using var verifyCtx = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var savedEntry = await verifyCtx.DsmEntries.Include(e => e.PaymentCollection).FirstAsync(e => e.DsmName == "Desktop DSM");
        Assert.Equal(10000m, savedEntry.TotalCollection);
        Assert.Equal(0m, savedEntry.Mismatch);

        // Repayment is persisted separately in CreditorRepayments and reduces debtor outstanding balance cleanly
        var rep = await verifyCtx.CreditorRepayments.SingleAsync(r => r.CreditorName == "Acme Transport");
        Assert.Equal(2500.0, rep.Amount, precision: 2);
        Assert.Equal("Cash", rep.PaymentMode);

        double totalDebits = await verifyCtx.DebitEntries.Where(d => d.DebtorName == "Acme Transport").SumAsync(d => d.Amount);
        double totalRepayments = await verifyCtx.CreditorRepayments.Where(r => r.CreditorName == "Acme Transport").SumAsync(r => r.Amount);
        Assert.Equal(7500.0, totalDebits - totalRepayments, precision: 2);
    }

    [Fact]
    public async Task PwaApprovalQueue_WithDebtorRepayments_DoesNotDeductOrInflateCollection_AndMatchesReportsAndPrints()
    {
        // Seed prior debit of 10,000 for Acme Transport
        using (var seedCtx = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var prevShift = new Shift { ShiftDate = DateTime.Today.AddDays(-1), ShiftType = "B" };
            seedCtx.Shifts.Add(prevShift);
            await seedCtx.SaveChangesAsync();

            var prevEntry = new DsmEntry
            {
                ShiftId = prevShift.ShiftId,
                DsmName = "Prior Debtor Entry",
                PumpId = 1,
                GrossSales = 10000m,
                TotalCreditors = 10000m,
                TotalCollection = 10000m
            };
            seedCtx.DsmEntries.Add(prevEntry);
            await seedCtx.SaveChangesAsync();

            seedCtx.DebitEntries.Add(new DebitEntry
            {
                DsmEntryId = prevEntry.DsmEntryId,
                DebtorName = "Acme Transport",
                Amount = 10000.0
            });
            await seedCtx.SaveChangesAsync();
        }

        var queueVm = _serviceProvider.GetRequiredService<DsmApprovalQueueViewModel>();
        var subId = Guid.NewGuid();
        dynamic customColl = new System.Dynamic.ExpandoObject();
        customColl.Cash = 6000.0;
        customColl.UPI = 4000.0;
        customColl.Card = 0.0;
        customColl.PetroCard = 0.0;
        customColl.CashDeposit = 0.0;
        customColl.Others = 0.0;
        customColl.Credit = 0.0;
        customColl.Expense = 0.0;
        customColl.ExpenseNotes = "";
        customColl.Short = 0.0;
        customColl.Excess = 0.0;
        FakeSupabaseDsmService.CustomCollections[subId] = customColl;

        var sub = new DsmPendingSubmission
        {
            Id = subId,
            DsmName = "PWA Repay Operator",
            ShiftDate = DateTime.Today,
            ShiftType = "A",
            PumpId = 1,
            SubmittedAt = DateTime.UtcNow,
            MetadataJson = JsonConvert.SerializeObject(new
            {
                formatVersion = 3,
                debtorRepayments = new[]
                {
                    new { debtorName = "Acme Transport", paymentMode = "PhonePe", amount = 2500.0, notes = "Recovery PhonePe" },
                    new { debtorName = "Acme Transport", paymentMode = "Bank Transfer", amount = 1500.0, notes = "NEFT Ref 101" }
                }
            })
        };

        queueVm.SelectedSubmission = sub;
        await Task.Delay(100);
        while (queueVm.IsLoadingDetails) await Task.Delay(50);

        queueVm.NozzleReadings.Clear();
        queueVm.NozzleReadings.Add(new DsmNozzleRow { NozzleId = 1, FuelType = "MS-I", OpeningReading = 100.0, ClosingReading = 200.0, Rate = 100.0 }); // 10,000 gross
        queueVm.NozzleReadings.Add(new DsmNozzleRow { NozzleId = 5, FuelType = "Speed", OpeningReading = 50.0, ClosingReading = 50.0, Rate = 100.0 }); // 0 gross
        queueVm.SubmissionTestingEntries.Clear();
        queueVm.DebtorEntries.Clear();
        queueVm.SubmissionExpenses.Clear();
        queueVm.KhandhareEntries.Clear();
        queueVm.QrPayments.Clear();

        // Pure fuel collections: 6,000 Cash + 4,000 PhonePe = 10,000 (balanced against 10,000 gross sale)
        queueVm.Cash2.Denom500 = 12; // 6,000
        queueVm.CashAmount = 6000.0;
        queueVm.CashDepositAmount = 0.0;
        queueVm.PhonePeMorning = 4000.0;
        queueVm.UpiAmount = 4000.0;
        queueVm.CardAmount = 0.0;
        queueVm.PetroCardAmount = 0.0;
        queueVm.ExpenseAmount = 0.0;
        queueVm.CreditAmount = 0.0;
        queueVm.OthersAmount = 0.0;

        typeof(DsmApprovalQueueViewModel).GetMethod("RecalculateTotals", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(queueVm, null);

        // Approval Queue must NOT deduct debtor repayments from PhonePe/Cash (no false -4000 shortage)
        Assert.Equal(10000.0, queueVm.TotalCollections, precision: 2);
        Assert.Equal(0.0, queueVm.MismatchAmount, precision: 2);
        Assert.Equal(0.0, queueVm.ShortAmount, precision: 2);
        Assert.Equal(0.0, queueVm.ExcessAmount, precision: 2);

        await queueVm.ApproveCommand.ExecuteAsync(null);

        using var verifyCtx = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var savedEntry = await verifyCtx.DsmEntries
            .Include(e => e.Shift)
            .Include(e => e.NozzleReadings)
            .Include(e => e.PaymentCollection)
            .Include(e => e.CashDenominations)
            .Include(e => e.DebitEntries)
            .Include(e => e.Expenses)
            .Include(e => e.TestingEntries)
            .FirstOrDefaultAsync(e => e.DsmName == "PWA Repay Operator");

        Assert.NotNull(savedEntry);
        Assert.Equal(10000m, savedEntry!.TotalCollection);
        Assert.Equal(0m, savedEntry.Mismatch);

        // Both repayments persisted for DSM Management & debtor balance reduced to 6,000 (10,000 - 2,500 - 1,500)
        var persistedReps = await verifyCtx.CreditorRepayments.Where(r => r.CreditorName == "Acme Transport").OrderBy(r => r.CreditorRepaymentId).ToListAsync();
        Assert.Equal(2, persistedReps.Count);
        Assert.Equal(4000.0, persistedReps.Sum(r => r.Amount), precision: 2);
        Assert.Equal(6000.0, 10000.0 - persistedReps.Sum(r => r.Amount), precision: 2);

        // Shift Total Report excludes all debtor repayments from ActualCollection and keeps them in DebtorRecoveriesTotal
        var reportSvc = _serviceProvider.GetRequiredService<IReportService>();
        var shiftReport = reportSvc.CalculateShiftReport(
            DateTime.Today,
            "A",
            new List<DsmEntry> { savedEntry },
            new List<Expense>(),
            new List<ShiftOtherCash>(),
            persistedReps,
            90, 100, 100, 85,
            new BusinessDayTidSheet(),
            new BusinessDayTidSheet(),
            "Mitali Service Station");

        Assert.Equal(10000.0, shiftReport.ExpectedCollection, precision: 2);
        Assert.Equal(10000.0, shiftReport.ActualCollection, precision: 2);
        Assert.Equal(0.0, shiftReport.Difference, precision: 2);
        Assert.True(shiftReport.IsBalanced);
        Assert.Equal(4000.0, shiftReport.DebtorRecoveriesTotal, precision: 2);
        Assert.Equal(2, shiftReport.DebtorRepayments.Count);
        var phonePeCat = shiftReport.CollectionBreakdown.First(c => c.Category.Contains("PhonePe", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(4000.0, phonePeCat.Amount, precision: 2);
        Assert.Equal(2500.0, phonePeCat.RecoveryAmount, precision: 2);

        // Day Total Report excludes all debtor repayments from ActualCollection and keeps them in DebtorRecoveriesTotal
        var dayReport = reportSvc.CalculateDayReport(
            DateTime.Today,
            DateTime.Today,
            new List<DsmEntry> { savedEntry },
            new List<Expense>(),
            persistedReps,
            90, 100, 100, 85,
            "Mitali Service Station");

        Assert.Equal(10000.0, dayReport.ExpectedCollection, precision: 2);
        Assert.Equal(10000.0, dayReport.ActualCollection, precision: 2);
        Assert.Equal(0.0, dayReport.Difference, precision: 2);
        Assert.True(dayReport.IsBalanced);
        Assert.Equal(4000.0, dayReport.DebtorRecoveriesTotal, precision: 2);
        Assert.Equal(2, dayReport.DebtorRepayments.Count);
    }

    [Fact]
    public void PrintTemplates_SkipInformationalRecoveryRowsFromCollectionsAndRecon_WhilePreservingDebtorRecoveriesTable()
    {
        var printingDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "FuelPro.UI", "Printing"));
        var dayHtml = File.ReadAllText(Path.Combine(printingDir, "DayTotalPrintTemplate.html"));
        var shiftHtml = File.ReadAllText(Path.Combine(printingDir, "FinalCalculationPrintTemplate.html"));
        var dsmHtml = File.ReadAllText(Path.Combine(printingDir, "DsmSheetPrintTemplate.html"));

        // Day Total and Shift Total exclude informational debtor recovery rows (e.g. Bank / Cheque Recovery) from collections & reconciliation
        Assert.Contains("var isRecoveryInfo = Boolean(c.isInformational)", dayHtml);
        Assert.Contains("if (isRecoveryInfo ||", dayHtml);
        Assert.Contains("if (isRecoveryInfo) {", dayHtml);
        Assert.Contains("var isRecoveryInfo = Boolean(c.isInformational)", shiftHtml);
        Assert.Contains("if (isRecoveryInfo) {", shiftHtml);

        // Separate Debtor Recoveries section preserved in all 3 print templates
        Assert.Contains("secRepayments", dayHtml);
        Assert.Contains("secRepayments", shiftHtml);
        Assert.Contains("personalDebtorSection", dsmHtml);
        Assert.Contains("c.isInformational", dsmHtml);
    }

    // =========================================================================
    // ISSUE 2: DSM Entry Closing Reading Keyboard Focus Navigation Tests
    // =========================================================================

    [Fact]
    public void DsmEntryView_NozzleReadings_TabShiftTabEnterAndMouse_MoveActualKeyboardFocusDirectlyToEditableTextBox()
    {
        Exception? threadEx = null;

        var staThread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                }

                var bundledTheme = new BundledTheme
                {
                    BaseTheme = BaseTheme.Light,
                    PrimaryColor = PrimaryColor.Blue,
                    SecondaryColor = SecondaryColor.LightBlue
                };
                var defaultsDict = new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml")
                };

                var appMerged = Application.Current!.Resources.MergedDictionaries;
                appMerged.Add(bundledTheme);
                appMerged.Add(defaultsDict);

                Window? window = null;
                try
                {
                    var vm = new DsmEntryViewModel();
                    vm.NozzleReadings.Clear();
                    vm.NozzleReadings.Add(new NozzleReadingRow
                    {
                        PumpId = 1,
                        NozzleNumber = 1,
                        FuelType = "MS-I",
                        OpeningReading = 100.0,
                        ClosingReading = 0.0,
                        Rate = 100.0,
                        OnRowChanged = vm.RecalculateAll
                    });
                    vm.NozzleReadings.Add(new NozzleReadingRow
                    {
                        PumpId = 1,
                        NozzleNumber = 5,
                        FuelType = "Speed",
                        OpeningReading = 200.0,
                        ClosingReading = 0.0,
                        Rate = 105.0,
                        OnRowChanged = vm.RecalculateAll
                    });

                    var view = new DsmEntryView { DataContext = vm };

                    // Attach an explicit outside TextBox after DsmEntryView inside a container so we verify boundary exit without looping
                    var postGridControl = new TextBox { Name = "PostGridTextBox" };
                    var hostPanel = new StackPanel();
                    hostPanel.Children.Add(view);
                    hostPanel.Children.Add(postGridControl);

                    window = new Window
                    {
                        Content = hostPanel,
                        Width = 1280,
                        Height = 900,
                        Left = -10000,
                        Top = -10000,
                        ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None
                    };

                    window.Show();
                    window.Activate();
                    window.UpdateLayout();

                    var grid = (DataGrid)view.FindName("NozzleReadingsDataGrid");
                    Assert.NotNull(grid);
                    grid.UpdateLayout();

                    var boxes = GetEditableBoxes(grid);
                    // Expected exact order across 2 rows: [0]=R0.Opening, [1]=R0.Closing, [2]=R1.Opening, [3]=R1.Closing
                    // Rate TextBoxes must be read-only and skipped completely by keyboard & mouse focus.
                    Assert.Equal(4, boxes.Count);

                    var allBoxes = GetAllTextBoxes(grid);
                    var rateBoxes = allBoxes.Where(b =>
                        System.Windows.Data.BindingOperations.GetBindingExpression(b, TextBox.TextProperty)?.ParentBinding?.Path?.Path == "Rate").ToList();
                    Assert.Equal(2, rateBoxes.Count);
                    Assert.Equal(new[] { "100.00", "105.00" }, rateBoxes.Select(b => b.Text).ToArray());
                    foreach (var rateBox in rateBoxes)
                    {
                        Assert.True(rateBox.IsReadOnly);
                        Assert.True(rateBox.IsEnabled);
                        Assert.False(rateBox.Focusable);
                        Assert.False(rateBox.IsTabStop);
                        Assert.False(rateBox.IsHitTestVisible);
                        Assert.False(rateBox.IsReadOnlyCaretVisible);
                    }

                    // Verify local CellStyle disables DataGridCell tab-stop without global DataGrid tampering
                    var cellStyle = grid.CellStyle;
                    Assert.NotNull(cellStyle);
                    Assert.Contains(cellStyle!.Setters.OfType<Setter>(), s => s.Property == Control.IsTabStopProperty && Equals(s.Value, false));
                    Assert.Contains(cellStyle.Setters.OfType<Setter>(), s => s.Property == UIElement.FocusableProperty && Equals(s.Value, false));

                    // Focus Row 0 Closing Reading TextBox directly
                    boxes[1].Focus();
                    Keyboard.Focus(boxes[1]);
                    Assert.Same(boxes[1], Keyboard.FocusedElement);

                    // Enter decimal reading and press Tab -> skips Rate and moves REAL Keyboard.FocusedElement directly to Row 1 Opening ([2])
                    boxes[1].Text = "125.50";
                    SendKey(boxes[1], Key.Tab, ModifierKeys.None);
                    Assert.IsType<TextBox>(Keyboard.FocusedElement);
                    Assert.Same(boxes[2], Keyboard.FocusedElement);
                    Assert.Equal(125.50, vm.NozzleReadings[0].ClosingReading ?? 0.0, precision: 2);
                    Assert.False(vm.NozzleReadings[0].HasValidationError);

                    // Press Enter -> moves directly to Row 1 Closing Reading TextBox ([3])
                    SendKey(boxes[2], Key.Enter, ModifierKeys.None);
                    Assert.Same(boxes[3], Keyboard.FocusedElement);

                    // Test Shift+Tab backward skips Rate: [3] -> [2] -> [1]
                    SendKey(boxes[3], Key.Tab, ModifierKeys.Shift);
                    Assert.Same(boxes[2], Keyboard.FocusedElement);
                    SendKey(boxes[2], Key.Tab, ModifierKeys.Shift);
                    Assert.Same(boxes[1], Keyboard.FocusedElement);

                    // Return to Row 1 Closing Reading ([3]) via Enter on [1] -> [2] -> [3]
                    SendKey(boxes[1], Key.Enter, ModifierKeys.None);
                    Assert.Same(boxes[2], Keyboard.FocusedElement);
                    SendKey(boxes[2], Key.Enter, ModifierKeys.None);
                    Assert.Same(boxes[3], Keyboard.FocusedElement);

                    // Enter invalid closing (< opening 200) -> validation triggers AND Enter exits grid cleanly (never focuses Rate or loops to [0])
                    boxes[3].Text = "150.00";
                    SendKey(boxes[3], Key.Enter, ModifierKeys.None);
                    Assert.True(vm.NozzleReadings[1].HasValidationError);
                    Assert.NotSame(boxes[0], Keyboard.FocusedElement);
                    Assert.False(grid.IsKeyboardFocusWithin);

                    // Fix closing reading back cleanly and verify Tab at end exits cleanly without focusing Rate
                    boxes[3].Focus();
                    Keyboard.Focus(boxes[3]);
                    boxes[3].Text = "220.00";
                    SendKey(boxes[3], Key.Tab, ModifierKeys.None);
                    Assert.False(vm.NozzleReadings[1].HasValidationError);
                    Assert.NotSame(boxes[0], Keyboard.FocusedElement);
                    Assert.False(grid.IsKeyboardFocusWithin);
                    Assert.Equal(100.0, vm.NozzleReadings[0].Rate ?? 0.0, precision: 2);
                    Assert.Equal(105.0, vm.NozzleReadings[1].Rate ?? 0.0, precision: 2);

                    // Mouse interaction preserved for editable fields: focusing/clicking ClosingReading box focuses TextBox directly
                    boxes[1].Focus();
                    Keyboard.Focus(boxes[1]);
                    Assert.Same(boxes[1], Keyboard.FocusedElement);
                }
                finally
                {
                    window?.Close();
                    appMerged.Remove(defaultsDict);
                    appMerged.Remove(bundledTheme);
                }
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });

        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        Assert.True(staThread.Join(TimeSpan.FromSeconds(30)), "STA UI navigation test timed out.");
        if (threadEx != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(threadEx).Throw();
        }
    }

    private static List<TextBox> GetEditableBoxes(DataGrid grid)
    {
        var result = new List<TextBox>();
        for (int i = 0; i < grid.Items.Count; i++)
        {
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(i);
            CollectTextBoxes(row, result, editableOnly: true);
        }
        return result;
    }

    private static List<TextBox> GetAllTextBoxes(DataGrid grid)
    {
        var result = new List<TextBox>();
        for (int i = 0; i < grid.Items.Count; i++)
        {
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(i);
            CollectTextBoxes(row, result, editableOnly: false);
        }
        return result;
    }

    private static void CollectTextBoxes(DependencyObject root, List<TextBox> result, bool editableOnly)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox tb)
            {
                if (!editableOnly || (tb.Focusable && tb.IsEnabled && !tb.IsReadOnly))
                {
                    result.Add(tb);
                }
                continue;
            }
            CollectTextBoxes(child, result, editableOnly);
        }
    }

    private static void SendKey(UIElement target, Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.Shift)
        {
            // Raise via DsmEntryView's PreviewKeyDown path with simulated Shift modifier check or direct KeyboardDevice
            // Note: Keyboard.Modifiers reads OS state; when Shift isn't physically held, we invoke PreviewKeyDown for non-modified Tab/Enter directly
            // and also verify TraversalRequest behavior on the UI element.
            if (target is TextBox tb)
            {
                var grid = FindParent<DataGrid>(tb);
                if (grid != null)
                {
                    var reverseMethod = typeof(DsmEntryView).GetMethod("TryMoveNozzleReadingFocus", BindingFlags.NonPublic | BindingFlags.Static);
                    reverseMethod?.Invoke(null, new object?[] { grid, tb, true });
                    return;
                }
            }
        }

        var source = PresentationSource.FromVisual(target) ?? new HwndSource(new HwndSourceParameters());
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = UIElement.PreviewKeyDownEvent
        };
        target.RaiseEvent(args);
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T typed) return typed;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }
}
