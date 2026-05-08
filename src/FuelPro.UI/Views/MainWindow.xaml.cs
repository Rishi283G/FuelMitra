using System.Windows;
using FuelPro.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FuelPro.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = App.Services.GetRequiredService<MainWindowViewModel>();
        DataContext = vm;
        vm.NavigateToDashboardCommand.Execute(null);
    }
}
