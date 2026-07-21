using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MarkdownVault.ViewModels;

namespace MarkdownVault.Views;

public partial class FileTreeView : UserControl
{
    public FileTreeView()
    {
        InitializeComponent();
    }

    private FileTreeViewModel? VM => DataContext as FileTreeViewModel;

    private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (VM is not null && e.NewValue is VaultFileNode node)
            VM.SelectedNode = node;
    }

    private void FileTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Read the node directly from the clicked element — more reliable than
        // relying on SelectedNode being updated before the double-click fires.
        var element = e.OriginalSource as FrameworkElement;
        var node    = element?.DataContext as VaultFileNode
                   ?? VM?.SelectedNode;

        if (node is { IsDirectory: false })
            VM?.OpenFileCommand.Execute(node);
    }
}
