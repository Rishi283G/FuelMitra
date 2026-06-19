using System.Windows;

namespace FuelPro.UI.Views;

public partial class DsmRejectionReasonWindow : Window
{
    public string RejectionReason { get; private set; } = string.Empty;

    public DsmRejectionReasonWindow()
    {
        InitializeComponent();
        ReasonTextBox.Focus();
    }

    private void Reject_Click(object sender, RoutedEventArgs e)
    {
        RejectionReason = ReasonTextBox.Text;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
