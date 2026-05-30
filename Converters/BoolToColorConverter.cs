using Microsoft.UI.Xaml.Data;
using Windows.UI;

namespace oru.Converters;

/// <summary>
/// Converts a boolean to a status color: green for true, dim gray for false.
/// </summary>
public sealed class BoolToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        var isTrue = value is true;
        return isTrue
            ? Color.FromArgb(255, 52, 211, 153)   // #34D399 green
            : Color.FromArgb(255, 90, 97, 117);   // #5A6175 dim gray
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
    {
        throw new NotSupportedException();
    }
}
