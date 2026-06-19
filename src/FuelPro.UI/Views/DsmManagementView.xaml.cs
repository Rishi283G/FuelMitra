using System.Windows.Controls;
using FuelPro.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FuelPro.UI.Views;

public partial class DsmManagementView : UserControl
{
    public DsmManagementView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<DsmManagementViewModel>();
    }
}
