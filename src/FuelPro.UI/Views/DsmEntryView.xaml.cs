using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.RegularExpressions;

namespace FuelPro.UI.Views;

public partial class DsmEntryView : UserControl
{
    private static readonly Regex DecimalInputRegex = new(@"^-?\d*([.,]\d*)?$");
    private static readonly Regex IntegerInputRegex  = new(@"^\d*$");

    public DsmEntryView() => InitializeComponent();

    // ── Decimal TextBox handlers ──────────────────────────────────────────

    private void DecimalTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        e.Handled = !IsValidDecimalText(GetProposedText(textBox, e.Text));
    }

    private void DecimalTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (!e.SourceDataObject.GetDataPresent(DataFormats.Text)) { e.CancelCommand(); return; }
        var pastedText = e.SourceDataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        if (!IsValidDecimalText(GetProposedText(textBox, pastedText))) e.CancelCommand();
    }

    private void DecimalTextBox_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (textBox.Text == "0.00" || textBox.Text == "0") textBox.Clear();
    }

    private void DecimalTextBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (string.IsNullOrWhiteSpace(textBox.Text)) { textBox.Text = "0.00"; return; }
        if (double.TryParse(textBox.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var value) ||
            double.TryParse(textBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
        { textBox.Text = value.ToString("0.00", CultureInfo.InvariantCulture); return; }
        textBox.Text = "0.00";
    }

    // ── Integer TextBox handlers (for denomination Qty inputs) ────────────

    private void IntegerTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        e.Handled = !IntegerInputRegex.IsMatch(GetProposedText(textBox, e.Text));
    }

    private void IntegerTextBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (string.IsNullOrWhiteSpace(textBox.Text)) { textBox.Text = "0"; return; }
        if (int.TryParse(textBox.Text, out var i)) { textBox.Text = i.ToString(); return; }
        textBox.Text = "0";
    }

    // ── Shared helpers ────────────────────────────────────────────────────

    private static string GetProposedText(TextBox textBox, string input)
    {
        var selectedLength = textBox.SelectionLength;
        var selectionStart = textBox.SelectionStart;
        var currentText = textBox.Text ?? string.Empty;

        return currentText.Remove(selectionStart, selectedLength).Insert(selectionStart, input);
    }

    private static bool IsValidDecimalText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        return DecimalInputRegex.IsMatch(text);
    }

    /// <summary>
    /// Bubble mouse-wheel events from DataGrid to the parent ScrollViewer
    /// so that the page scrolls instead of the DataGrid trapping the event.
    /// </summary>
    private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DataGrid dataGrid) return;

        // Find the parent ScrollViewer and forward the event
        var parent = dataGrid.Parent as UIElement;
        while (parent != null && parent is not ScrollViewer)
        {
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent) as UIElement;
        }

        if (parent is ScrollViewer scrollViewer)
        {
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = sender
            };
            scrollViewer.RaiseEvent(eventArg);
            e.Handled = true;
        }
    }

    /// <summary>
    /// When the user presses Enter on any input, move focus to the next focusable element.
    /// This enables keyboard-only navigation through the entire DSM Entry form.
    /// </summary>
    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var element = Keyboard.FocusedElement as UIElement;
            if (element != null)
            {
                // Update binding on current textbox before moving
                if (element is TextBox tb)
                {
                    var binding = tb.GetBindingExpression(TextBox.TextProperty);
                    binding?.UpdateSource();
                }

                element.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// When the user presses Enter inside the Testing DataGrid, move focus to the next
    /// editable TextBox cell (Litres → Rate → next-row Litres, etc.).
    /// </summary>
    private void TestingDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (Keyboard.FocusedElement is not TextBox currentTb) return;

        // Update the current textbox's binding before leaving
        var binding = currentTb.GetBindingExpression(TextBox.TextProperty);
        binding?.UpdateSource();

        bool isLitres = binding?.ParentBinding?.Path?.Path == "Litres";

        // Move to the next focusable element (usually Rate)
        currentTb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));

        // If we were in Litres and just moved to Rate, skip Rate and go to next row's Litres
        if (isLitres && Keyboard.FocusedElement is TextBox nextTb)
        {
            var nextBinding = nextTb.GetBindingExpression(TextBox.TextProperty);
            if (nextBinding?.ParentBinding?.Path?.Path == "Rate")
            {
                nextTb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
        }

        e.Handled = true;
    }

    private void DsmComboBox_DropDownOpened(object sender, EventArgs e)
    {
        if (DataContext is ViewModels.DsmEntryViewModel vm)
        {
            _ = vm.LoadSuggestionsAsync();
        }
    }
}
