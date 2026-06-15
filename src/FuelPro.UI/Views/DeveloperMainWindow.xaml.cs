using System.Windows;
using FuelPro.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FuelPro.UI.Views;

public partial class DeveloperMainWindow : Window
{
    public DeveloperMainWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<DeveloperMainWindowViewModel>();
    }
}
