using System.Windows.Controls;
using FuelPro.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FuelPro.UI.Views;

public partial class OpeningBalanceManagementView : UserControl
{
    public OpeningBalanceManagementView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext == null && App.Services != null)
            {
                var vm = App.Services.GetService<OpeningBalanceManagementViewModel>();
                if (vm != null)
                {
                    DataContext = vm;
                    await vm.LoadDataAsync();
                }
            }
            else if (DataContext is OpeningBalanceManagementViewModel vm)
            {
                await vm.LoadDataAsync();
            }
        };
    }
}
