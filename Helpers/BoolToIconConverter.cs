using System.Globalization;
using System.Windows.Data;

namespace MarkdownVault.Helpers;

/// <summary>Returns a Segoe MDL2 Assets glyph: folder (E8B7) for <c>true</c>, file (E8A5) for <c>false</c>.</summary>
[ValueConversion(typeof(bool), typeof(string))]
public sealed class BoolToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "\uE8B7" : "\uE8A5";   // Folder / Document

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
