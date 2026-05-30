using Microsoft.UI.Xaml.Data;
using System;

namespace oru.Converters;

public sealed class BoolToOpacityConverter : IValueConverter
{
    public double TrueOpacity { get; set; } = 1.0;
    public double FalseOpacity { get; set; } = 0.6;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        if (value is bool b)
        {
            var result = invert ? !b : b;
            return result ? TrueOpacity : FalseOpacity;
        }
        return TrueOpacity;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
