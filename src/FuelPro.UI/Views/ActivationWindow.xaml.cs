using System.Windows;
using FuelPro.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FuelPro.UI.Views
{
    /// <summary>
    /// Interaction logic for ActivationWindow.xaml
    /// </summary>
    public partial class ActivationWindow : Window
    {
        private readonly ActivationViewModel _vm;

        public ActivationWindow()
        {
            InitializeComponent();
            _vm = App.Services.GetRequiredService<ActivationViewModel>();
            DataContext = _vm;

            _vm.ActivationSucceeded += OnActivationSuccess;
        }

        private void OnActivationSuccess()
        {
            var loginView = new LoginView();
            loginView.Show();
            Close();
        }
    }
}
