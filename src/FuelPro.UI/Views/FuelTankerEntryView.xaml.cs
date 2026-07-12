using System.Windows.Controls;

namespace FuelPro.UI.Views;

public partial class FuelTankerEntryView : UserControl
{
    public FuelTankerEntryView()
    {
        InitializeComponent();
        DataContext = new ViewModels.FuelTankerEntryViewModel();
    }
}
