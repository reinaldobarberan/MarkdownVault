using System.Windows;
using System.Windows.Controls;

namespace MarkdownVault.Views;

/// <summary>
/// Hosts the graph <see cref="GraphCanvas"/> plus its floating overlays (filters,
/// legend, zoom). The zoom buttons drive the canvas camera directly.
/// </summary>
public partial class GraphView : UserControl
{
    public GraphView()
    {
        InitializeComponent();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)  => Canvas.ZoomIn();
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Canvas.ZoomOut();
    private void Reset_Click(object sender, RoutedEventArgs e)   => Canvas.ResetView();
}
