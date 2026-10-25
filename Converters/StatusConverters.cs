using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PharmacyInventorySystem.Models;

namespace PharmacyInventorySystem.Converters
{
    /// <summary>Converts a Diff decimal (or ProductStatus) into the matching background brush (green/yellow/red).</summary>
    public class DiffToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            decimal diff = value switch { decimal d => d, int i => i, _ => 0m };
            string key = diff == 0 ? "PositiveBackBrush" : diff > 0 ? "WarningBackBrush" : "NegativeBackBrush";
            return Application.Current.Resources[key];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Converts a Diff decimal into the matching foreground text brush.</summary>
    public class DiffToForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            decimal diff = value switch { decimal d => d, int i => i, _ => 0m };
            string key = diff == 0 ? "PositiveTextBrush" : diff > 0 ? "WarningTextBrush" : "NegativeTextBrush";
            return Application.Current.Resources[key];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Formats a Diff decimal with an explicit "+" sign for positive values (e.g. "+20", "-40", "0").</summary>
    public class DiffToSignedStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            decimal diff = value switch { decimal d => d, int i => i, _ => 0m };
            var text = diff.ToString("0.##");
            return diff > 0 ? $"+{text}" : text;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>True (Dark) -> Moon glyph, False (Light) -> Sun glyph, for the theme toggle button.</summary>
    public class BoolToThemeGlyphConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? "\uE708" : "\uE706";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Shows the placeholder TextBlock only while the bound string is empty (used behind the search TextBox).</summary>
    public class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
