// Views/Converters.cs
using System.Globalization;
using System.Windows.Data;

namespace PaddleOcrDesktop.Views;

public class FileNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string path ? System.IO.Path.GetFileName(path) : value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
