using System.Windows.Controls;
using FuelPro.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FuelPro.UI.Views;

public partial class DsmApprovalQueueView : UserControl
{
    public DsmApprovalQueueView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<DsmApprovalQueueViewModel>();
    }
}
