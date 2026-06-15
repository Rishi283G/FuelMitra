using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Common;
using FuelPro.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FuelPro.UI.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly AuthService _authService;

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _pin = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isLoading;

    /// <summary>
    /// The role of the user who just logged in.
    /// </summary>
    public UserRole LoggedInRole { get; private set; }

    public event Action? LoginSucceeded;

    public LoginViewModel()
    {
        _authService = App.Services.GetRequiredService<AuthService>();
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Pin))
        {
            ErrorMessage = "Please enter username and PIN";
            return;
        }

        IsLoading = true;
        try
        {
            var result = await _authService.LoginAsync(Username, Pin);
            if (result.Success)
            {
                LoggedInRole = result.Data!.ParsedRole;
                LoginSucceeded?.Invoke();
            }
            else
            {
                ErrorMessage = result.Error;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}

