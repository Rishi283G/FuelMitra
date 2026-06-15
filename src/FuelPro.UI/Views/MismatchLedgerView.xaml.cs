using System.Windows.Controls;
using FuelPro.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FuelPro.UI.Views;

public partial class MismatchLedgerView : UserControl
{
    public MismatchLedgerView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<MismatchLedgerViewModel>();
    }
}
