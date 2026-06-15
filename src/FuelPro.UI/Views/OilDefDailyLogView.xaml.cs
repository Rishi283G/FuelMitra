using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Input;

namespace FuelPro.UI.Views;

public partial class OilDefDailyLogView : UserControl
{
    public OilDefDailyLogView()
    {
        InitializeComponent();
    }

    private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
    {
        Regex regex = new Regex("[^0-9.]+");
        e.Handled = regex.IsMatch(e.Text);
    }
}
