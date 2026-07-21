using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MarkdownVault.Views;

/// <summary>
/// Modal dialog that lets the user search and select a vault file to insert
/// as an internal link.  Returns the formatted Markdown text to insert.
/// </summary>
public partial class LinkPickerDialog : Window
{
    private readonly List<string> _allFiles;
    private readonly string? _currentFilePath;
    private readonly string? _vaultRoot;

    /// <summary>The Markdown text to insert at the caret (e.g. <c>[[notas]]</c>).</summary>
    public string ResultMarkdown { get; private set; } = string.Empty;

    /// <param name="vaultFiles">Relative paths of every file in the vault.</param>
    /// <param name="currentFilePath">Absolute path of the file being edited.</param>
    /// <param name="vaultRoot">Absolute path of the vault root directory.</param>
    public LinkPickerDialog(
        List<string> vaultFiles,
        string? currentFilePath,
        string? vaultRoot)
    {
        InitializeComponent();
        _allFiles        = vaultFiles;
        _currentFilePath = currentFilePath;
        _vaultRoot       = vaultRoot;

        FileList.ItemsSource = _allFiles;
        Loaded += (_, _) => SearchBox.Focus();
    }

    // ─── Search filtering ────────────────────────────────────────────────────

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            FileList.ItemsSource = _allFiles;
        }
        else
        {
            FileList.ItemsSource = _allFiles
                .Where(f => f.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (FileList.Items.Count > 0)
            FileList.SelectedIndex = 0;
    }

    // ─── Keyboard navigation ─────────────────────────────────────────────────

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (FileList.Items.Count > 0)
                {
                    FileList.SelectedIndex = Math.Min(
                        FileList.SelectedIndex + 1, FileList.Items.Count - 1);
                    FileList.ScrollIntoView(FileList.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (FileList.Items.Count > 0)
                {
                    FileList.SelectedIndex = Math.Max(FileList.SelectedIndex - 1, 0);
                    FileList.ScrollIntoView(FileList.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Enter:
                TryInsert();
                e.Handled = true;
                break;
        }
    }

    // ─── Selection ───────────────────────────────────────────────────────────

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        TryInsert();
    }

    private void Insert_Click(object sender, RoutedEventArgs e)
    {
        TryInsert();
    }

    private void TryInsert()
    {
        if (FileList.SelectedItem is not string selectedRelPath)
            return;

        var useWikiLink = WikiLinkRadio.IsChecked == true;
        ResultMarkdown = BuildLinkMarkdown(selectedRelPath, useWikiLink);
        DialogResult   = true;
    }

    // ─── Link formatting ─────────────────────────────────────────────────────

    private string BuildLinkMarkdown(string selectedRelPath, bool useWikiLink)
    {
        // Calculate path relative to the current file's directory.
        string linkTarget;
        if (_currentFilePath is not null && _vaultRoot is not null)
        {
            var currentDir = Path.GetDirectoryName(_currentFilePath)!;
            var absoluteSelected = Path.GetFullPath(
                Path.Combine(_vaultRoot, selectedRelPath.Replace('/', '\\')));
            linkTarget = Path.GetRelativePath(currentDir, absoluteSelected)
                             .Replace('\\', '/');
        }
        else
        {
            linkTarget = selectedRelPath;
        }

        if (useWikiLink)
        {
            var name = Path.GetFileNameWithoutExtension(selectedRelPath);

            // A bare [[name]] is enough when the filename is unique in the vault.
            // When it isn't (duplicate names in different folders), include the
            // vault-relative path so the link resolves to the file you picked.
            bool nameIsUnique = _allFiles.Count(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f), name,
                    StringComparison.OrdinalIgnoreCase)) == 1;

            if (nameIsUnique)
                return $"[[{name}]]";

            var relNoExt = selectedRelPath;
            var dot = relNoExt.LastIndexOf('.');
            if (dot >= 0) relNoExt = relNoExt[..dot];
            return $"[[{relNoExt}]]";
        }
        else
        {
            // [display](relative/path.md) — use filename as display text.
            // Wrap the destination in angle brackets when it has spaces/parens so
            // Markdown recognizes it as a link (CommonMark rule).
            var display = Path.GetFileNameWithoutExtension(selectedRelPath);
            var href    = linkTarget.IndexOfAny([' ', '(', ')']) >= 0 ? $"<{linkTarget}>" : linkTarget;
            return $"[{display}]({href})";
        }
    }
}
