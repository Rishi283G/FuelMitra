using System.Windows;

namespace Rashtra.LicenseGenerator
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            System.AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as System.Exception;
                MessageBox.Show($"Fatal Exception: {ex?.Message}\n\nStack Trace:\n{ex?.StackTrace}", 
                    "License Generator Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            base.OnStartup(e);
        }
    }
}
