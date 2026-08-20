using System.Globalization;
using System.Windows.Data;
using Binding = System.Windows.Data.Binding;

namespace MundoBrowser;

public static class Converters
{
    public static readonly IValueConverter NullToBool = new NullToBoolConverter();
}

public class NullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
