using CommunityToolkit.Mvvm.ComponentModel;

namespace FuelPro.UI.ViewModels;

/// <summary>
/// Short Recovery placeholder — to be refined with actual short tracking logic later.
/// </summary>
public partial class ShortRecoveryViewModel : ObservableObject
{
    [ObservableProperty] private DateTime _selectedMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private string _statusMessage = "Short recovery tracking module will be configured here.\nThis feature is under development.";
    [ObservableProperty] private bool _isLoading;

    public ShortRecoveryViewModel()
    {
        // Placeholder — logic will be added later
    }
}
