using System.Windows;
using System.Windows.Controls;
using FuelPro.UI.ViewModels;

namespace FuelPro.UI.Views;

public partial class AgsImportView : UserControl
{
    public AgsImportView()
    {
        InitializeComponent();
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AgsImportViewModel vm)
            _ = vm.LoadHistoryCommand.ExecuteAsync(null);
    }

    private void ShiftRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is AgsImportViewModel vm && sender is RadioButton rb)
            vm.SelectedShift = rb.Tag?.ToString() ?? "A";
    }
}
