using System.Windows;
using System.Windows.Controls;
using MarkdownVault.ViewModels;

namespace MarkdownVault.Views;

/// <summary>Elige la plantilla de un item de barra de plugins: botón único o dropdown.</summary>
public sealed class PluginToolbarItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? SingleTemplate { get; set; }
    public DataTemplate? GroupTemplate  { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        => item is PluginToolbarItemViewModel { IsGroup: true } ? GroupTemplate : SingleTemplate;
}
