using System;
using System.Globalization;
using System.Windows.Data;

namespace PharmacyInventorySystem.Converters
{
    public sealed class DiffSignConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal d)
                return d < 0m ? "Negative" : d > 0m ? "Positive" : "Zero";

            if (value is double dbl)
                return dbl < 0d ? "Negative" : dbl > 0d ? "Positive" : "Zero";

            if (value is int i)
                return i < 0 ? "Negative" : i > 0 ? "Positive" : "Zero";

            return "Zero";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
