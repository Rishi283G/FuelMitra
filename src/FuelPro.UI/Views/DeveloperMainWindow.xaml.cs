using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv && !e.Handled)
        {
            sv.ScrollToVerticalOffset(sv.VerticalOffset - (e.Delta / 2.0));
            e.Handled = true;
        }
    }
}

