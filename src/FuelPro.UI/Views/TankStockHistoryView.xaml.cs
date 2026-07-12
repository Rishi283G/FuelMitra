using System.Windows.Controls;

namespace FuelPro.UI.Views;

public partial class TankStockHistoryView : UserControl
{
    public TankStockHistoryView()
    {
        InitializeComponent();
        DataContext = new ViewModels.TankStockHistoryViewModel();
    }
}
