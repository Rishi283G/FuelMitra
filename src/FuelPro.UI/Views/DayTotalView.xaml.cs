using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using FuelPro.UI.Printing;
using FuelPro.UI.ViewModels;

namespace FuelPro.UI.Views;

public partial class DayTotalView : UserControl
{
    public DayTotalView()
    {
        InitializeComponent();
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DayTotalViewModel vm && !vm.IsLoading)
            vm.RefreshCommand.Execute(null);
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DayTotalViewModel vm) return;

        if (vm.LoadingState == FuelPro.Core.DTOs.ReportLoadingState.Loading)
        {
            MessageBox.Show("Day data is currently loading. Please wait.", "Print",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (vm.LoadingState == FuelPro.Core.DTOs.ReportLoadingState.Error)
        {
            MessageBox.Show($"Cannot print: Failed to load day data.\n\nError: {vm.LoadingErrorMessage}",
                "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (vm.LoadingState != FuelPro.Core.DTOs.ReportLoadingState.Loaded || !vm.HasData || vm.CurrentReport == null)
        {
            MessageBox.Show("No data loaded to print.", "Print",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            new PrintService().PrintDayTotal(vm.CurrentReport);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Print failed.\n\nError: {ex.Message}",
                "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
