using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SelfDesk.Viewer.Converters;

/// <summary>Converts TransferProgress (int, -1 = no transfer in progress) → Visibility.</summary>
public sealed class ProgressVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int i && i >= 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
