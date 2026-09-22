using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdownVault.Services;

namespace MarkdownVault.Views;

/// <summary>
/// Modal dialog that lets the user search and select a vault file, then (link-anchors change,
/// Phase 6) an optional anchor within it — the whole note, a heading, or an already-marked
/// paragraph — to insert as an internal link. Returns the formatted Markdown text to insert.
/// Never writes anything to disk: a block marker is created only via "Copiar enlace a este
/// párrafo" in the focused editor buffer (<c>EditorView.xaml.cs</c>), never from here (spec
/// "Marker-Writing Boundary" — the picker's target note is not necessarily the focused one).
/// </summary>
public partial class LinkPickerDialog : Window
{
    private readonly List<string>     _allFiles;
    private readonly string?          _currentFilePath;
    private readonly string?          _vaultRoot;
    private readonly FileService?     _fileService;
    private readonly MarkdownService? _markdownService;

    private string?           _selectedRelPath;
    private List<AnchorOption> _anchorOptions = new();
    private bool               _onAnchorStep;

    /// <summary>The Markdown text to insert at the caret (e.g. <c>[[notas]]</c> or
    /// <c>[[notas#Sección]]</c>).</summary>
    public string ResultMarkdown { get; private set; } = string.Empty;

    /// <param name="vaultFiles">Relative paths of every file in the vault.</param>
    /// <param name="currentFilePath">Absolute path of the file being edited.</param>
    /// <param name="vaultRoot">Absolute path of the vault root directory.</param>
    /// <param name="fileService">Reads the picked note's content for the anchor step. Null in
    /// tests that only exercise the plain file-picking step.</param>
    /// <param name="markdownService">Parses the picked note the SAME way the preview renders it,
    /// so heading/block ids never drift (design decision #6). Null in tests that only exercise
    /// the plain file-picking step.</param>
    public LinkPickerDialog(
        List<string> vaultFiles,
        string? currentFilePath,
        string? vaultRoot,
        FileService? fileService = null,
        MarkdownService? markdownService = null)
    {
        InitializeComponent();
        _allFiles        = vaultFiles;
        _currentFilePath = currentFilePath;
        _vaultRoot       = vaultRoot;
        _fileService     = fileService;
        _markdownService = markdownService;

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
                TryAdvanceToAnchorStep();
                e.Handled = true;
                break;
        }
    }

    // ─── Selection (step 1: pick a file) ──────────────────────────────────────

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        TryAdvanceToAnchorStep();
    }

    private void Insert_Click(object sender, RoutedEventArgs e)
    {
        if (_onAnchorStep) TryInsertAnchor();
        else                TryAdvanceToAnchorStep();
    }

    private void TryAdvanceToAnchorStep()
    {
        if (FileList.SelectedItem is not string selectedRelPath)
            return;

        _selectedRelPath = selectedRelPath;
        ShowAnchorStep(selectedRelPath);
    }

    // ─── Step 2: pick an anchor (link-anchors change, Phase 6) ────────────────

    /// <summary>What one row of the anchor list means: <c>Anchor == null</c> is the whole note
    /// (no <c>#</c> suffix at all); otherwise it's the raw anchor half exactly as
    /// <c>Helpers.LinkTarget.Parse</c> would produce it — heading text as written, or a block id
    /// with its <c>^</c> sigil still attached. <c>IsSelectable = false</c> marks an
    /// informational-only row (read error, or the "no marked paragraphs yet" hint) that Insert
    /// must ignore.</summary>
    private sealed record AnchorOption(string Label, string? Anchor, bool IsSelectable = true);

    private void ShowAnchorStep(string selectedRelPath)
    {
        _anchorOptions = BuildAnchorOptions(selectedRelPath);

        AnchorNoteLabel.Text  = $"Enlazar a: {Path.GetFileNameWithoutExtension(selectedRelPath)}";
        AnchorList.ItemsSource = _anchorOptions;
        AnchorList.SelectedIndex = 0;

        _onAnchorStep = true;
        SearchHeader.Visibility    = Visibility.Collapsed;
        AnchorNoteLabel.Visibility = Visibility.Visible;
        FileList.Visibility        = Visibility.Collapsed;
        AnchorList.Visibility      = Visibility.Visible;
        BackButton.Visibility      = Visibility.Visible;
        AnchorList.Focus();
    }

    /// <summary>
    /// Builds the second step's options: whole note, then headings, then paragraphs that
    /// ALREADY carry a block marker — read from the picked note's file on disk, via the SAME
    /// owning root the picker itself was scoped to. Zero markers still shows the note and
    /// headings normally, plus a plain-language hint instead of a hidden/empty section (spec
    /// "Target note has no marked paragraphs"). Never writes anything.
    /// </summary>
    private List<AnchorOption> BuildAnchorOptions(string selectedRelPath)
    {
        var options = new List<AnchorOption> { new("Nota completa (sin ancla)", null) };

        if (_fileService is null || _markdownService is null || _vaultRoot is null)
            return options; // no service seam wired (e.g. a test exercising only step 1) — degrade to whole-note-only

        string content;
        try
        {
            var absolutePath = Path.GetFullPath(
                Path.Combine(_vaultRoot, selectedRelPath.Replace('/', '\\')));
            content = _fileService.ReadFile(absolutePath);
        }
        catch (Exception ex)
        {
            options.Add(new AnchorOption($"No se pudo leer la nota ({ex.Message}).", null, IsSelectable: false));
            return options;
        }

        foreach (var heading in _markdownService.GetHeadings(content))
        {
            if (string.IsNullOrWhiteSpace(heading.Text)) continue;
            options.Add(new AnchorOption($"# {heading.Text}", heading.Text));
        }

        var markers = _markdownService.GetBlockMarkers(content);
        if (markers.Count == 0)
        {
            options.Add(new AnchorOption(
                "Esta nota no tiene párrafos marcados todavía. Para crear uno, abrila, hacé " +
                "clic derecho sobre un párrafo y elegí «Copiar enlace a este párrafo».",
                null, IsSelectable: false));
        }
        else
        {
            var doc = _markdownService.ParsePreviewAst(content);
            var byLine = new Dictionary<int, ParagraphBlock>();
            foreach (var p in doc.Descendants<ParagraphBlock>())
                byLine.TryAdd(p.Line, p);

            foreach (var marker in markers)
            {
                var preview = byLine.TryGetValue(marker.Line, out var paragraph)
                    ? ParagraphPreview(paragraph)
                    : $"(línea {marker.Line + 1})";
                options.Add(new AnchorOption($"¶ {preview}", "^" + marker.Id));
            }
        }

        return options;
    }

    private static string ParagraphPreview(ParagraphBlock paragraph)
    {
        var sb = new System.Text.StringBuilder();
        if (paragraph.Inline is not null)
            foreach (var literal in paragraph.Inline.Descendants<LiteralInline>())
                sb.Append(literal.Content.ToString());

        var text = sb.ToString().Trim();
        if (text.Length == 0) return "(párrafo sin texto)";
        return text.Length > 60 ? text[..60] + "…" : text;
    }

    private void AnchorList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => TryInsertAnchor();

    private void AnchorList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryInsertAnchor();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ReturnToFileStep();
            e.Handled = true;
        }
    }

    private void TryInsertAnchor()
    {
        if (_selectedRelPath is null) return;
        if (AnchorList.SelectedItem is not AnchorOption option || !option.IsSelectable) return;

        var useWikiLink = WikiLinkRadio.IsChecked == true;
        ResultMarkdown = BuildLinkMarkdown(_selectedRelPath, useWikiLink, option.Anchor);
        DialogResult   = true;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ReturnToFileStep();

    private void ReturnToFileStep()
    {
        _onAnchorStep = false;
        SearchHeader.Visibility    = Visibility.Visible;
        AnchorNoteLabel.Visibility = Visibility.Collapsed;
        FileList.Visibility        = Visibility.Visible;
        AnchorList.Visibility      = Visibility.Collapsed;
        BackButton.Visibility      = Visibility.Collapsed;
        SearchBox.Focus();
    }

    // ─── Link formatting ─────────────────────────────────────────────────────

    /// <param name="anchor">The raw anchor half exactly as it should appear after <c>#</c> in
    /// the built link — <c>null</c> for a plain whole-note link, heading text as written, or a
    /// block id with its <c>^</c> sigil attached (e.g. <c>"^a1b2c3"</c>).</param>
    private string BuildLinkMarkdown(string selectedRelPath, bool useWikiLink, string? anchor)
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

            var notePart = nameIsUnique ? name : StripExtension(selectedRelPath);
            return anchor is null ? $"[[{notePart}]]" : $"[[{notePart}#{anchor}]]";
        }
        else
        {
            // [display](relative/path.md) — use filename as display text.
            // Wrap the destination in angle brackets when it has spaces/parens so
            // Markdown recognizes it as a link (CommonMark rule).
            var display = Path.GetFileNameWithoutExtension(selectedRelPath);
            var href    = anchor is null ? linkTarget : $"{linkTarget}#{anchor}";
            href = href.IndexOfAny([' ', '(', ')']) >= 0 ? $"<{href}>" : href;
            return $"[{display}]({href})";
        }
    }

    private static string StripExtension(string relPath)
    {
        var dot = relPath.LastIndexOf('.');
        return dot >= 0 ? relPath[..dot] : relPath;
    }
}
