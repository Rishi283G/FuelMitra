using System.Windows;
using FuelPro.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FuelPro.UI.Views;

public partial class LoginView : Window
{
    private readonly LoginViewModel _vm;

    public LoginView()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<LoginViewModel>();
        DataContext = _vm;

        _vm.LoginSucceeded += OnLoginSuccess;

        // PasswordBox doesn't support binding - wire manually
        PinBox.PasswordChanged += (_, _) => _vm.Pin = PinBox.Password;
    }

    private void OnLoginSuccess()
    {
        var mainWindow = new MainWindow();
        mainWindow.Show();
        Close();
    }
}
