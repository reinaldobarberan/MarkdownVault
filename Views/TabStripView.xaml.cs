using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MarkdownVault.Models;
using MarkdownVault.ViewModels;

namespace MarkdownVault.Views;

/// <summary>
/// Reusable file-tab strip bound to an <see cref="EditorGroupViewModel"/> (its ambient
/// DataContext). Extracted from <see cref="EditorView"/> so the same strip can appear both
/// inside each editor pane and above the preview in Solo visor. Click switches tabs,
/// middle-click closes them — the group's commands do the actual work.
/// </summary>
public partial class TabStripView : UserControl
{
    public TabStripView()
    {
        InitializeComponent();
    }

    private EditorGroupViewModel? Group => DataContext as EditorGroupViewModel;

    /// <summary>Left-click on a tab → switch to it.</summary>
    private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Group is null) return;
        if (sender is FrameworkElement fe && fe.DataContext is OpenTab tab)
        {
            Group.SwitchToTabCommand.Execute(tab);
            e.Handled = true;
        }
    }

    /// <summary>Middle-click on a tab → close it.</summary>
    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Group is null) return;
        if (e.ChangedButton == MouseButton.Middle &&
            sender is FrameworkElement fe && fe.DataContext is OpenTab tab)
        {
            Group.CloseTabCommand.Execute(tab);
            e.Handled = true;
        }
    }
}
