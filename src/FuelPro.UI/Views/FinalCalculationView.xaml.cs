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
        if (DataContext is not FinalCalculationViewModel vm || !vm.HasData || vm.CurrentReport == null)
        {
            MessageBox.Show("No shift data loaded to print.",
                "Print", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var printService = new PrintService();
            printService.PrintFinalCalculation(vm.CurrentReport);
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
        => rows.FirstOrDefault(r => r.Description == description || r.Description.StartsWith(description + " ("))?.Amount ?? 0;

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
                case "Cash 1": dto.CashDepositTotal = row.TotalAmount; break;
            }
        }

        dto.GrandTotal = cashType == "Cash1" ? vm.Cash1Total : vm.Cash2Total;
        return dto;
    }
}
