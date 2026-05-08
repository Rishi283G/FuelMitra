using System.Windows;
using System.Windows.Navigation;
using Serilog;

namespace FuelPro.UI.Printing;

/// <summary>
/// A hidden WPF window that loads an HTML file in the WebBrowser control.
/// After the page finishes loading (LoadCompleted), window.print() is called.
/// The window closes automatically after the print dialog closes.
/// </summary>
public partial class PrintWindow : Window
{
    private readonly string _htmlFilePath;
    private readonly ILogger _logger = Log.ForContext<PrintWindow>();
    private bool _printed;

    public PrintWindow(string htmlFilePath)
    {
        _htmlFilePath = htmlFilePath;
        InitializeComponent();
        Loaded += PrintWindow_Loaded;
    }

    private void PrintWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Navigate to the temp HTML file (data already injected)
            PrintBrowser.Navigate(new Uri(_htmlFilePath));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PrintWindow navigation failed");
            Close();
        }
    }

    private void PrintBrowser_LoadCompleted(object sender, NavigationEventArgs e)
    {
        if (_printed) return;
        _printed = true;

        try
        {
            // window.onload has already called renderData().
            // Now trigger the browser's print dialog.
            PrintBrowser.InvokeScript("print");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PrintBrowser InvokeScript(print) failed");
        }
        finally
        {
            Close();
        }
    }
}

