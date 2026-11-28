using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace FuelPro.UI.Helpers;

/// <summary>
/// Standardizes all desktop WPF DatePicker controls to DD/MM/YYYY display and input parsing
/// regardless of Windows OS regional settings, without altering Thread.CurrentCulture or
/// numeric/currency formatting elsewhere in the application.
/// </summary>
public static class DatePickerFormatHelper
{
    public const string DisplayFormat = "dd/MM/yyyy";

    private static readonly string[] AcceptedParseFormats =
    {
        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd-MM-yyyy",
        "d-M-yyyy",
        "dd/MM/yy",
        "d/M/yy",
        "yyyy-MM-dd"
    };

    private static readonly XmlLanguage EnGbLanguage = XmlLanguage.GetLanguage("en-GB");
    private static readonly object RegistrationLock = new();
    private static readonly ConditionalWeakTable<DatePicker, object> HookedDatePickers = new();
    private static bool _isRegistered;

    public static string FormatDate(DateTime? date) =>
        date.HasValue ? date.Value.ToString(DisplayFormat, CultureInfo.InvariantCulture) : string.Empty;

    public static bool TryParseDateText(string? text, out DateTime parsedDate)
    {
        parsedDate = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        return DateTime.TryParseExact(
            text.Trim(),
            AcceptedParseFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsedDate);
    }

    public static void RegisterGlobalFormat()
    {
        lock (RegistrationLock)
        {
            if (_isRegistered) return;
            _isRegistered = true;
        }

        // Attach via ClassHandlers on Initialized/Loaded/SelectedDateChanged rather than OverrideMetadata
        // on LanguageProperty before DatePicker's static constructor runs, preserving Thread.CurrentCulture
        // and numeric/currency formatting while ensuring every desktop DatePicker uses en-GB (dd/MM/yyyy).
        EventManager.RegisterClassHandler(
            typeof(DatePicker),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnDatePickerLoaded));

        EventManager.RegisterClassHandler(
            typeof(DatePicker),
            DatePicker.SelectedDateChangedEvent,
            new EventHandler<SelectionChangedEventArgs>(OnSelectedDateChanged));
    }

    /// <summary>
    /// Applies the standardized DD/MM/YYYY language and handlers immediately to a DatePicker instance
    /// (called automatically by Loaded class handler, and directly usable for pre-Loaded instances).
    /// </summary>
    public static void ApplyTo(DatePicker dp)
    {
        if (dp == null) return;
        dp.Language = EnGbLanguage;
        if (!HookedDatePickers.TryGetValue(dp, out _))
        {
            HookedDatePickers.Add(dp, new object());
            dp.CalendarClosed += OnCalendarClosed;
            dp.DateValidationError += OnDateValidationError;
        }
        EnsureTextBoxHooked(dp);
        SyncDisplayText(dp);
    }

    private static void OnDatePickerLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DatePicker dp)
        {
            ApplyTo(dp);
        }
    }

    private static void OnSelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not DatePicker dp) return;
        SyncDisplayText(dp);
    }

    private static void OnCalendarClosed(object? sender, RoutedEventArgs e)
    {
        if (sender is not DatePicker dp) return;
        SyncDisplayText(dp);
    }

    private static void OnDateValidationError(object? sender, DatePickerDateValidationErrorEventArgs e)
    {
        if (sender is not DatePicker dp) return;
        if (TryParseDateText(e.Text, out var parsed))
        {
            e.ThrowException = false;
            dp.SelectedDate = parsed;
            SyncDisplayText(dp);
        }
    }

    private static void EnsureTextBoxHooked(DatePicker dp)
    {
        if (FindDatePickerTextBox(dp) is not { } tb) return;

        if (!Equals(tb.Tag, "DatePickerDdMmYyyyHooked"))
        {
            tb.Tag = "DatePickerDdMmYyyyHooked";
            tb.LostKeyboardFocus += (_, _) => CommitTextBoxInput(dp, tb);
            tb.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter || args.Key == Key.Return)
                {
                    CommitTextBoxInput(dp, tb);
                }
            };
        }
    }

    private static void CommitTextBoxInput(DatePicker dp, DatePickerTextBox tb)
    {
        if (TryParseDateText(tb.Text, out var parsed))
        {
            if (dp.SelectedDate?.Date != parsed.Date)
            {
                dp.SelectedDate = parsed;
            }
            tb.Text = FormatDate(parsed);
        }
        else if (dp.SelectedDate.HasValue && string.IsNullOrWhiteSpace(tb.Text))
        {
            dp.SelectedDate = null;
        }
        else if (dp.SelectedDate.HasValue)
        {
            tb.Text = FormatDate(dp.SelectedDate);
        }
    }

    internal static void SyncDisplayText(DatePicker dp)
    {
        if (dp.SelectedDate is not { } date) return;
        var textBox = FindDatePickerTextBox(dp);
        if (textBox == null || textBox.IsKeyboardFocusWithin) return;

        var formatted = FormatDate(date);
        if (!string.Equals(textBox.Text, formatted, StringComparison.Ordinal))
        {
            textBox.Text = formatted;
        }
    }

    private static DatePickerTextBox? FindDatePickerTextBox(DatePicker dp)
    {
        if (dp.Template?.FindName("PART_TextBox", dp) is DatePickerTextBox named)
        {
            return named;
        }

        return FindVisualChild<DatePickerTextBox>(dp);
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) return typed;
            var nested = FindVisualChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }
}
