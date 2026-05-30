using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace oru.Converters;

/// <summary>
/// Converts a boolean to a SolidColorBrush: green for true (connected), dim gray for false.
/// </summary>
public sealed class BoolToColorBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        var isTrue = value is true;
        var color = isTrue
            ? Color.FromArgb(255, 16, 137, 62)    // #10893E – Windows green
            : Color.FromArgb(255, 139, 147, 165);  // #8B93A5 – dim gray
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
    {
        throw new NotSupportedException();
    }
}
