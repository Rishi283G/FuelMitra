using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FuelPro.UI.Converters;

/// <summary>
/// Formats amounts in Indian currency: ₹1,23,456.00
/// </summary>
public class IndianCurrencyConverter : IValueConverter
{
    private static readonly CultureInfo IndianCulture = new("en-IN");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
            return $"₹{d.ToString("N2", IndianCulture)}";
        if (value is decimal dec)
            return $"₹{dec.ToString("N2", IndianCulture)}";
        return "₹0.00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Formats litres to 4 decimal places.
/// </summary>
public class LitreFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d) return $"{d:F4} L";
        return "0.0000 L";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Green brush if value is 0, Red if non-zero.
/// </summary>
public class DifferenceColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
            return Math.Abs(d) < 0.01
                ? new SolidColorBrush(Color.FromRgb(76, 175, 80))   // Green
                : new SolidColorBrush(Color.FromRgb(244, 67, 54));  // Red
        return new SolidColorBrush(Colors.White);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Standard bool to Visibility converter.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>
/// Inverted bool to Visibility (true = collapsed).
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}

/// <summary>
/// Returns "Balanced ✓", "Extra ₹X" (total > gross), or "Short ₹X" (total < gross) badge text.
/// </summary>
public class DifferenceBadgeConverter : IValueConverter
{
    private static readonly CultureInfo IndianCulture = new("en-IN");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            if (Math.Abs(d) < 0.01) return "Balanced ✓";
            return d > 0
                ? $"Extra ₹{d.ToString("N2", IndianCulture)}"
                : $"Short ₹{Math.Abs(d).ToString("N2", IndianCulture)}";
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns green background for balanced, red for short/excess.
/// </summary>
public class DifferenceBadgeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
            return Math.Abs(d) < 0.01
                ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
                : new SolidColorBrush(Color.FromRgb(244, 67, 54));
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Inverts a boolean value.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}

/// <summary>
/// Returns 0 PumpId as empty string for the total row.
/// </summary>
public class PumpIdDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int id && id > 0) return $"Pump {id}";
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns a background brush based on ImportWarningSeverity.
/// Error = Red, Warning = Amber, Info = Blue-grey.
/// </summary>
public class ImportWarningToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FuelPro.Core.DTOs.ImportWarningSeverity severity)
        {
            return severity switch
            {
                FuelPro.Core.DTOs.ImportWarningSeverity.Error   => new SolidColorBrush(Color.FromArgb(40, 244, 67, 54)),
                FuelPro.Core.DTOs.ImportWarningSeverity.Warning => new SolidColorBrush(Color.FromArgb(40, 255, 152, 0)),
                _                                               => new SolidColorBrush(Color.FromArgb(30, 33, 150, 243)),
            };
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns Visibility.Visible when a string is non-null and non-empty.
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts an integer denomination quantity to its rupee amount.
/// ConverterParameter = denomination face value (e.g. "500", "200").
/// Used to show auto-calculated Amount column in Cash In Hand table.
/// </summary>
public class DenomAmountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var qty = value switch
        {
            int i => (double)i,
            double d => d,
            null => 0.0,
            _ => 0.0
        };

        if (parameter is string ps && double.TryParse(ps, NumberStyles.Any, CultureInfo.InvariantCulture, out var denom))
            return $"{qty * denom:N2}";

        return $"{qty:N2}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
