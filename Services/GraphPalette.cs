namespace MarkdownVault.Services;

/// <summary>
/// Colours for the graph's groups. Lives in the services layer as plain hex strings so both the
/// canvas (which freezes brushes from them) and the legend (which binds them straight to a
/// <c>Brush</c> property) read from ONE list — a second copy in XAML would drift the day someone
/// adds a colour.
/// </summary>
public static class GraphPalette
{
    // Amber (#E0AF68) is deliberately absent: that is the active-note colour, and a group wearing
    // it would make "the note you are editing" impossible to spot. Hues are interleaved so two
    // consecutive groups never land on neighbouring colours.
    private static readonly string[] Colors =
    [
        "#6F9FD8", // azul
        "#7FB069", // verde
        "#C678DD", // violeta
        "#D08770", // terracota
        "#56B6C2", // cian
        "#BF616A", // granate
        "#A3BE8C", // salvia
        "#B48EAD", // malva
        "#88C0D0", // celeste
        "#E06C75", // coral
    ];

    /// <summary>Colour of the notes that carry no group (index &lt; 0).</summary>
    public const string Fallback = "#6F9FD8";

    public static int Count => Colors.Length;

    /// <summary>Hex colour for a group index, wrapping around once there are more groups than colours.</summary>
    public static string HexFor(int groupIndex) =>
        groupIndex < 0 ? Fallback : Colors[groupIndex % Colors.Length];
}
