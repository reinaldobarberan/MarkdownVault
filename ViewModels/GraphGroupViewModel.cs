using CommunityToolkit.Mvvm.ComponentModel;
using MarkdownVault.Services;

namespace MarkdownVault.ViewModels;

/// <summary>
/// One row of the graph legend: a vault area (top-level folder), its colour, how many notes it
/// holds, and whether it is currently switched on. Toggling a row raises
/// <see cref="VisibilityChanged"/> so the owning <see cref="GraphViewModel"/> can re-run the
/// filter — the row itself knows nothing about the graph.
/// </summary>
public partial class GraphGroupViewModel : ObservableObject
{
    public GraphGroupViewModel(GraphGroup group)
    {
        Index    = group.Index;
        Name     = group.Name;
        Count    = group.Count;
        ColorHex = GraphPalette.HexFor(group.Index);
    }

    /// <summary>Stable group index — also the palette slot.</summary>
    public int Index { get; }

    /// <summary>Folder name shown in the legend.</summary>
    public string Name { get; }

    /// <summary>Notes in this area.</summary>
    public int Count { get; }

    /// <summary>Hex colour; XAML converts it straight to a Brush.</summary>
    public string ColorHex { get; }

    [ObservableProperty] private bool _isVisible = true;

    /// <summary>Raised when the user switches this area on or off.</summary>
    public event Action? VisibilityChanged;

    partial void OnIsVisibleChanged(bool value) => VisibilityChanged?.Invoke();
}
