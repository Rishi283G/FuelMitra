using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FuelPro.UI.ViewModels;

namespace FuelPro.UI.Views;

public partial class SalaryCalculationView : UserControl
{
    public SalaryCalculationView()
    {
        InitializeComponent();
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SalaryCalculationViewModel vm)
        {
            vm.SelectedProfile = null;
        }
    }

    private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
    {
        Regex regex = new Regex("[^0-9.]+");
        e.Handled = regex.IsMatch(e.Text);
    }
}
