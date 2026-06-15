using System.Windows.Controls;
using FuelPro.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FuelPro.UI.Views;

public partial class ExpenseAnalysisView : UserControl
{
    public ExpenseAnalysisView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<ExpenseAnalysisViewModel>();
    }
}
