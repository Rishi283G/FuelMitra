using System.Windows;
using FuelPro.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FuelPro.UI.Views;

public partial class OwnerMainWindow : Window
{
    public OwnerMainWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<OwnerMainWindowViewModel>();
    }
}
