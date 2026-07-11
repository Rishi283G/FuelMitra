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
        if (DataContext is DayTotalViewModel vm && !vm.HasData)
            vm.RefreshCommand.Execute(null);
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DayTotalViewModel vm || !vm.HasData || vm.CurrentReport == null)
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
