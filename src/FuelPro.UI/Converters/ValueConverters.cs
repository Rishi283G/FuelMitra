using System;
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
/// Formats litres to 2 decimal places for dashboard display.
/// </summary>
public class LitresConverter : IValueConverter
{
    private static readonly CultureInfo IndianCulture = new("en-IN");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d) return $"{d.ToString("N2", IndianCulture)} L";
        return "0.00 L";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Formats Kg to 4 decimal places.
/// </summary>
public class KgFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d) return $"{d:F4} Kg";
        return "0.0000 Kg";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Formats Kg to 2 decimal places for dashboard display.
/// </summary>
public class KgConverter : IValueConverter
{
    private static readonly CultureInfo IndianCulture = new("en-IN");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d) return $"{d.ToString("N2", IndianCulture)} Kg";
        return "0.00 Kg";
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

/// <summary>
/// Converts null/non-null to Visibility.
/// Parameter = "Inverted" to reverse the behavior.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isNull = value == null;
        bool invert = parameter?.ToString() == "Inverted";

        bool visible = invert ? isNull : !isNull;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts equality of value and parameter to Visibility.
/// </summary>
public class EqualToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return Visibility.Collapsed;
        bool equal = string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        return equal ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns true when value.ToString() equals parameter.ToString().
/// Used for button highlighting (IsDefault/style switching) and DatePicker IsEnabled gating.
/// </summary>
public class EqualToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts an integer count to a Brush.
/// If count > 0, returns red/orange brush based on parameter.
/// If count == 0, returns green/grey brush.
/// </summary>
public class IntToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            if (count > 0)
            {
                var colorStr = parameter?.ToString()?.ToLowerInvariant();
                return colorStr switch
                {
                    "red" => new SolidColorBrush(Color.FromRgb(229, 57, 53)),     // #E53935
                    "orange" => new SolidColorBrush(Color.FromRgb(245, 124, 0)),   // #F57C00
                    _ => new SolidColorBrush(Color.FromRgb(229, 57, 53))
                };
            }
            return new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green (#4CAF50)
        }
        return new SolidColorBrush(Color.FromRgb(120, 144, 156)); // Grey (#78909C)
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts an integer to Visibility (visible if > 0).
/// </summary>
public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count && count > 0)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns Neon Green for Cash/PhonePe/Card, Orange for Cheque/Bank Transfer, Red for DSM Short.
/// </summary>
public class PaymentCategoryToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush NeonGreen = new(Color.FromRgb(0, 200, 83)); // #00C853
    private static readonly SolidColorBrush Orange = new(Color.FromRgb(255, 152, 0));   // #FF9800
    private static readonly SolidColorBrush Red = new(Color.FromRgb(211, 47, 47));      // #D32F2F
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(33, 33, 33));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string cat)
        {
            var lower = cat.ToLowerInvariant();
            if (lower.Contains("cash") || lower.Contains("phonepe") || lower.Contains("card") || lower.Contains("upi"))
                return NeonGreen;
            if (lower.Contains("cheque") || lower.Contains("bank") || lower.Contains("transfer"))
                return Orange;
            if (lower.Contains("short") || lower.Contains("loss"))
                return Red;
        }
        return DefaultBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns Green for Petrol (MS-I, MS-II, MS, Petrol), Orange for Diesel (HSD), Purple for CNG.
/// </summary>
public class FuelTypeToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush NeonGreenBrush = new(Color.FromRgb(0, 200, 83)); // Neon Green (#00C853)
    private static readonly SolidColorBrush BlueBrush = new(Color.FromRgb(25, 118, 210));   // Blue (#1976D2)
    private static readonly SolidColorBrush PurpleBrush = new(Color.FromRgb(123, 31, 162)); // Purple (#7B1FA2)
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(15, 76, 129)); // #0F4C81

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string fuel)
        {
            var upper = fuel.ToUpperInvariant();
            if (upper.Contains("MS") || upper.Contains("PETROL"))
                return NeonGreenBrush;
            if (upper.Contains("HSD") || upper.Contains("DIESEL"))
                return BlueBrush;
            if (upper.Contains("CNG"))
                return PurpleBrush;
        }
        return DefaultBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class FuelTypeToLightBgBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush LightGreenBrush = new(Color.FromRgb(232, 245, 233)); // Light Green (#E8F5E9)
    private static readonly SolidColorBrush LightBlueBrush = new(Color.FromRgb(227, 242, 253));  // Light Blue (#E3F2FD)
    private static readonly SolidColorBrush LightPurpleBrush = new(Color.FromRgb(243, 229, 245)); // Light Purple (#F3E5F5)
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(250, 250, 250));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string fuel)
        {
            var upper = fuel.ToUpperInvariant();
            if (upper.Contains("MS") || upper.Contains("PETROL"))
                return LightGreenBrush;
            if (upper.Contains("HSD") || upper.Contains("DIESEL"))
                return LightBlueBrush;
            if (upper.Contains("CNG"))
                return LightPurpleBrush;
        }
        return DefaultBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
