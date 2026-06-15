using System.Windows;
using FuelPro.Core.Common;
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
        Window nextWindow;

        if (_vm.LoggedInRole == UserRole.Owner)
        {
            nextWindow = new OwnerMainWindow();
        }
        else if (_vm.LoggedInRole == UserRole.Developer)
        {
            nextWindow = new DeveloperMainWindow();
        }
        else
        {
            // Manager role (and legacy Operator/Admin) uses MainWindow
            nextWindow = new MainWindow();
        }

        nextWindow.Show();
        Close();
    }
}

