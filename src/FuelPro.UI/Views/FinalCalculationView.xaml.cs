using System.Windows;
using System.Windows.Controls;
using FuelPro.Core.DTOs;
using FuelPro.Core.Services;
using FuelPro.UI.Printing;
using FuelPro.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FuelPro.UI.Views;

public partial class FinalCalculationView : UserControl
{
    public FinalCalculationView()
    {
        InitializeComponent();
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is FinalCalculationViewModel vm && !vm.HasData)
        {
            vm.LoadShiftDataCommand.Execute(null);
        }
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FinalCalculationViewModel vm || !vm.HasData)
        {
            MessageBox.Show("No shift data loaded to print.",
                "Print", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            // Retrieve station name from settings
            string stationName = "VKD Petroleum";
            try
            {
                var settingsRepo = App.Services.GetRequiredService<FuelPro.Core.Repositories.ISettingsRepository>();
                var settingsResult = settingsRepo.GetSettingsAsync().GetAwaiter().GetResult();
                if (settingsResult.Success && settingsResult.Data != null)
                    stationName = settingsResult.Data.PumpStationName;
            }
            catch { /* use default */ }

            // Extract reconciliation row amounts by description (no recalculation)
            var reconRows = vm.ReconciliationRows.ToList();
            double recMsTesting  = GetReconAmount(reconRows, "MS Testing");
            double recHsdTesting = GetReconAmount(reconRows, "HSD Testing");
            double recPhonePeCardMorning = GetReconAmount(reconRows, "Phone Pe Card (Morning)");
            double recPhonePeCardNight   = GetReconAmount(reconRows, "Phone Pe Card (Night)");
            double recPhonePeMorning = GetReconAmount(reconRows, "Phone Pe (Morning)");
            double recPhonePeNight   = GetReconAmount(reconRows, "Phone Pe (Night)");
            double recPCard      = GetReconAmount(reconRows, "P. Card");
            double recDebit      = GetReconAmount(reconRows, "Debit");
            double recCCard      = GetReconAmount(reconRows, "PineLab Card");
            double recBank       = GetReconAmount(reconRows, "Bank Cash");
            double recHand       = GetReconAmount(reconRows, "Cash In Hand");

            var builder = new PrintDataBuilder();

            var printData = builder.BuildPrintData(
                date:                     vm.SelectedDate,
                shiftType:                vm.SelectedShift,
                stationName:              stationName,
                dsmRows:                  vm.DsmSummaryRows.ToList(),
                cash1Agg:                 BuildCashAgg(vm, "Cash1"),
                cash2Agg:                 BuildCashAgg(vm, "Cash2"),
                creditorRows:             vm.CreditorRows.ToList(),
                hsdLitres:                vm.HsdLitres,
                hsdRate:                  vm.HsdRate,
                hsdAmount:                vm.HsdAmount,
                msILitres:                vm.MsILitres,
                msIRate:                  vm.MsIRate,
                msIAmount:                vm.MsIAmount,
                msIILitres:               vm.MsIILitres,
                msIIRate:                 vm.MsIIRate,
                msIIAmount:               vm.MsIIAmount,
                otherCashTotal:           vm.OtherCashTotal,
                reconciliationMsTesting:  recMsTesting,
                reconciliationHsdTesting: recHsdTesting,
                phonePeTotal:             recPhonePeMorning + recPhonePeNight + recPhonePeCardMorning + recPhonePeCardNight,
                phonePeMorningTotal:      recPhonePeMorning,
                phonePeNightTotal:        recPhonePeNight,
                phonePeCardTotal:         recPhonePeCardMorning + recPhonePeCardNight,
                phonePeCardMorningTotal:  recPhonePeCardMorning,
                phonePeCardNightTotal:    recPhonePeCardNight,
                creditCardTotal:          recCCard,
                petroCardTotal:           recPCard,
                bankCash:                 recBank,
                cashInHand:               recHand,
                expensesTotal:            vm.ExpensesTotal,
                reconciliationTotal:      vm.ReconciliationTotal,
                grossFuelSaleTotal:       vm.GrossSaleTotal,
                expenseRows:              vm.ExpenseRows.ToList());

            var printService = new PrintService();
            printService.PrintFinalCalculation(printData);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Print failed:\n" + ex.Message,
                "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    private static double GetReconAmount(List<ReconciliationRowDto> rows, string description)
        => rows.FirstOrDefault(r => r.Description == description)?.Amount ?? 0;

    /// <summary>
    /// Reconstructs a <see cref="CashAggregateDto"/> from the denomination display rows
    /// already computed by the ViewModel — zero recalculation.
    /// </summary>
    private static CashAggregateDto BuildCashAgg(FinalCalculationViewModel vm, string cashType)
    {
        var dto  = new CashAggregateDto();
        var rows = cashType == "Cash1" ? vm.Cash1Rows : vm.Cash2Rows;

        foreach (var row in rows)
        {
            switch (row.Denomination)
            {
                case "₹500": dto.Total500   = row.TotalCount; break;
                case "₹200": dto.Total200   = row.TotalCount; break;
                case "₹100": dto.Total100   = row.TotalCount; break;
                case "₹50":  dto.Total50    = row.TotalCount; break;
                case "₹20":  dto.Total20    = row.TotalCount; break;
                case "₹10":  dto.Total10    = row.TotalCount; break;
                case "Coin": dto.TotalCoins = row.TotalCount; break;
                case "Cash Deposit": dto.CashDepositTotal = row.TotalAmount; break;
            }
        }

        dto.GrandTotal = cashType == "Cash1" ? vm.Cash1Total : vm.Cash2Total;
        return dto;
    }
}
