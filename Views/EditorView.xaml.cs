using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using MarkdownVault.Helpers;
using MarkdownVault.Models;
using MarkdownVault.Services;
using MarkdownVault.ViewModels;

namespace MarkdownVault.Views;

/// <summary>
/// Editor panel: wires AvalonEdit (which has no DP for Text) to
/// <see cref="EditorGroupViewModel"/> manually, registers the Markdown
/// syntax highlighting definition on first load, and manages tab events.
/// </summary>
public partial class EditorView : UserControl, IFindReplaceTarget
{
    private EditorGroupViewModel? _vm;
    private bool             _updatingFromVm;

    // ─── Spell checking ───────────────────────────────────────────────────────
    // Underlines are applied by a line colorizer (see SpellCheckColorizer), which
    // re-runs automatically whenever AvalonEdit rebuilds visual lines.
    private SpellCheckColorizer? _spellColorizer;

    public EditorView()
    {
        InitializeComponent();
        RegisterMarkdownHighlighting();
        TextEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Markdown");

        SetupSpellCheck();

        DataContextChanged += OnDataContextChanged;

        // Phase 4 focus tracking (design §5.1 / C5): PreviewMouseDown TUNNELS, so it fires
        // for a press anywhere in this pane — including the tab strip and toolbar, which are
        // Border/TextBlock/ItemsControl and Focusable=false, so they never raise
        // GotKeyboardFocus. GotKeyboardFocus is still needed for Tab-key traversal and
        // programmatic TextEditor.Focus() calls elsewhere in this file. Neither alone covers
        // both cases — both are wired.
        PreviewMouseDown += (_, _) => (DataContext as EditorGroupViewModel)?.NotifyFocused();
        GotKeyboardFocus  += (_, _) => (DataContext as EditorGroupViewModel)?.NotifyFocused();
    }

    /// <summary>Registers the spell-check colorizer when a dictionary is available.</summary>
    private void SetupSpellCheck()
    {
        if (App.SpellCheckService is not { IsAvailable: true }) return;

        _spellColorizer = new SpellCheckColorizer(App.SpellCheckService);
        TextEditor.TextArea.TextView.LineTransformers.Add(_spellColorizer);

        // Right-click on a misspelled word → context menu with replacement suggestions.
        // Wired once (not per-DataContext) since it only touches the editor, not the VM.
        TextEditor.PreviewMouseRightButtonDown += TextEditor_PreviewMouseRightButtonDown;
    }

    // ─── VM wiring ────────────────────────────────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged      -= Vm_PropertyChanged;
            _vm.InsertionRequested   -= Vm_InsertionRequested;
            _vm.SnippetRequested     -= Vm_SnippetRequested;
            _vm.ReplaceSelectionRequested -= Vm_ReplaceSelectionRequested;
            _vm.ActiveTabChanged     -= OnActiveTabChanged;
            _vm.ActiveTabSaving      -= OnActiveTabSaving;
            TextEditor.TextChanged   -= Editor_TextChanged;
            TextEditor.PreviewMouseLeftButtonDown -= TextEditor_PreviewMouseLeftButtonDown;
        }

        _vm = DataContext as EditorGroupViewModel;

        if (_vm is null) return;

        _vm.PropertyChanged    += Vm_PropertyChanged;
        _vm.InsertionRequested += Vm_InsertionRequested;
        _vm.SnippetRequested   += Vm_SnippetRequested;
        _vm.ReplaceSelectionRequested += Vm_ReplaceSelectionRequested;
        _vm.ActiveTabChanged   += OnActiveTabChanged;
        _vm.ActiveTabSaving    += OnActiveTabSaving;
        // Let plugin commands read the live selection from AvalonEdit.
        _vm.SelectedTextProvider = () => TextEditor.SelectedText;
        TextEditor.TextChanged += Editor_TextChanged;
        TextEditor.PreviewMouseLeftButtonDown += TextEditor_PreviewMouseLeftButtonDown;

        // Sync initial content.
        SetEditorText(_vm.Content);
        ApplyFont(_vm);
        UpdateSyntaxHighlighting();
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null) return;

        switch (e.PropertyName)
        {
            case nameof(EditorGroupViewModel.Content):
                if (!_updatingFromVm)
                    SetEditorText(_vm.Content);
                break;
            case nameof(EditorGroupViewModel.CurrentFilePath):
                UpdateSyntaxHighlighting();
                break;
        }
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        _updatingFromVm = true;
        _vm.Content     = TextEditor.Text;
        UpdateCaret();
        _updatingFromVm = false;
        // The colorizer re-checks changed lines automatically as AvalonEdit rebuilds
        // their visual lines — no explicit re-run needed here.
    }

    // ─── Tab switching ───────────────────────────────────────────────────────

    /// <summary>Saves scroll/caret state into the outgoing tab before a switch.</summary>
    private void OnActiveTabSaving(OpenTab? tab)
    {
        if (tab is null) return;
        tab.ScrollOffset = (int)TextEditor.VerticalOffset;
        tab.CaretOffset  = TextEditor.CaretOffset;
    }

    /// <summary>Restores the editor state when switching to a new tab.</summary>
    private void OnActiveTabChanged(OpenTab? tab)
    {
        _updatingFromVm = true;

        if (tab is null)
        {
            SetEditorText(string.Empty);
            TextEditor.SyntaxHighlighting = null;
            _updatingFromVm = false;
            return;
        }

        SetEditorText(tab.Content);
        UpdateSyntaxHighlighting();

        // Restore caret and scroll after content is loaded.
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (tab.CaretOffset <= TextEditor.Document.TextLength)
                    TextEditor.CaretOffset = tab.CaretOffset;

                TextEditor.ScrollToVerticalOffset(tab.ScrollOffset);
            }
            catch { /* ignore if offset is stale */ }
        }, System.Windows.Threading.DispatcherPriority.Loaded);

        _updatingFromVm = false;
    }

    // ─── Toolbar insertion ────────────────────────────────────────────────────

    /// <summary>Inserts a complete snippet verbatim at the caret (used by the Mermaid examples).</summary>
    private void Vm_SnippetRequested(string text)
    {
        var editor = TextEditor;

        // Start on a fresh line if the caret isn't already at the beginning of one.
        var line   = editor.Document.GetLineByOffset(editor.CaretOffset);
        var prefix = editor.CaretOffset > line.Offset ? "\n" : "";

        editor.Document.Insert(editor.CaretOffset, prefix + text);
        editor.Focus();
    }

    /// <summary>Replaces the current selection (or inserts at caret) — used by plugin commands.</summary>
    private void Vm_ReplaceSelectionRequested(string text)
    {
        var editor = TextEditor;
        if (editor.SelectionLength > 0)
            editor.Document.Replace(editor.SelectionStart, editor.SelectionLength, text);
        else
            editor.Document.Insert(editor.CaretOffset, text);
        editor.Focus();
    }

    private void Vm_InsertionRequested(string prefix, string suffix)
    {
        var editor    = TextEditor;
        var selection = editor.SelectedText;

        // Fenced code block: prefix ends with \n (e.g. "```csharp\n")
        if (prefix.Contains('\n'))
        {
            var inner   = string.IsNullOrEmpty(selection) ? "// code here" : selection;
            var block   = prefix + inner + suffix;
            var offset  = string.IsNullOrEmpty(selection)
                ? editor.CaretOffset
                : editor.SelectionStart;
            var length  = string.IsNullOrEmpty(selection) ? 0 : editor.SelectionLength;

            editor.Document.Replace(offset, length, block);
            // Place caret on the code line (after opening fence + newline).
            editor.CaretOffset = offset + prefix.Length;
            editor.SelectionStart  = offset + prefix.Length;
            editor.SelectionLength = inner.Length;
            editor.Focus();
            return;
        }

        // Line-prefix only (e.g. "# ", "- "): insert at start of current line.
        if (string.IsNullOrEmpty(suffix))
        {
            var line = editor.Document.GetLineByOffset(editor.CaretOffset);
            editor.Document.Insert(line.Offset, prefix);
            editor.CaretOffset = line.Offset + prefix.Length;
            editor.Focus();
            return;
        }

        // Inline wrap (bold, italic, link, inline-code).
        var inner2  = string.IsNullOrEmpty(selection) ? "text" : selection;
        var wrapped = prefix + inner2 + suffix;
        if (!string.IsNullOrEmpty(selection))
            editor.Document.Replace(editor.SelectionStart, editor.SelectionLength, wrapped);
        else
            editor.Document.Insert(editor.CaretOffset, wrapped);
        editor.Focus();
    }

    // ─── Keyboard shortcuts ───────────────────────────────────────────────────

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_vm is null) { base.OnPreviewKeyDown(e); return; }

        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Esta rama SÍ se ejecuta desde que SaveCommand declara CanExecute. Mientras estuvo
            // «siempre habilitado», el else era código muerto y Ctrl+S sin archivo abría el
            // diálogo de Guardar como sobre un contenido que no pertenecía a ninguna pestaña.
            if (_vm.SaveCommand.CanExecute(null))
                _vm.SaveCommand.Execute(null);
            else
                MessageBox.Show(
                    "No hay ningún archivo abierto. Abrí uno desde el explorador o creá uno nuevo con Ctrl+N.",
                    "Nada que guardar", MessageBoxButton.OK, MessageBoxImage.Information);
            e.Handled = true;
            return;
        }

        // Los atajos NO pasan por el CanExecute del botón: RelayCommand.Execute no lo consulta.
        // Sin este corte, Ctrl+V/Ctrl+B/Ctrl+I seguirían escribiendo en el panel vacío por
        // Document.Insert, que se saltea el IsReadOnly de AvalonEdit.
        if (!_vm.HasOpenDocument) { base.OnPreviewKeyDown(e); return; }

        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control
            && Clipboard.ContainsImage())
        {
            PasteClipboardImage();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
        { _vm.InsertBoldCommand.Execute(null);   e.Handled = true; return; }
        if (e.Key == Key.I && Keyboard.Modifiers == ModifierKeys.Control)
        { _vm.InsertItalicCommand.Execute(null); e.Handled = true; return; }

        base.OnPreviewKeyDown(e);
    }

    // ─── Buscar / Reemplazar ─────────────────────────────────────────────────
    // Implementación explícita de IFindReplaceTarget: el formulario de búsqueda opera
    // sobre ESTE panel cuando es el que tiene el foco (ver MainWindow.ResolveFindTarget).
    // Explícita, no pública, para no ensanchar la superficie de EditorView — nadie más
    // debería andar moviendo la selección desde afuera.

    /// <summary>True cuando este panel tiene una pestaña abierta sobre la cual buscar.
    /// Delega en el VM: una sola definición de «hay documento» para búsqueda, edición y
    /// habilitación de la barra.</summary>
    internal bool HasOpenDocument => _vm?.HasOpenDocument == true;

    /// <summary>Devuelve el foco al área de texto (al cerrar el formulario de búsqueda).</summary>
    internal void FocusEditor() => TextEditor.Focus();

    string IFindReplaceTarget.Text => TextEditor.Text;

    // AvalonEdit ya devuelve el offset del cursor cuando la selección está vacía, pero se
    // deja explícito: de este contrato depende que "buscar siguiente" arranque donde está
    // el cursor y no en el offset 0.
    int IFindReplaceTarget.SelectionStart =>
        TextEditor.SelectionLength > 0 ? TextEditor.SelectionStart : TextEditor.CaretOffset;

    int IFindReplaceTarget.SelectionLength => TextEditor.SelectionLength;

    void IFindReplaceTarget.SelectAndReveal(int offset, int length) =>
        SelectAndReveal(offset, length);

    void IFindReplaceTarget.ReplaceAndReveal(int offset, int length, string replacement)
    {
        var doc = TextEditor.Document;
        if (doc is null || offset < 0 || length < 0 || offset + length > doc.TextLength) return;

        doc.Replace(offset, length, replacement);
        // Deja seleccionado lo recién insertado: así la búsqueda siguiente arranca DESPUÉS
        // del reemplazo y no vuelve a caer sobre él cuando el reemplazo contiene al patrón.
        SelectAndReveal(offset, replacement.Length);
    }

    int IFindReplaceTarget.ApplyReplacements(IReadOnlyList<TextReplacement> replacements)
    {
        var doc = TextEditor.Document;
        if (doc is null || replacements.Count == 0) return 0;

        var applied = 0;

        // BeginUpdate/EndUpdate agrupa TODO en una sola operación de deshacer: un
        // "Reemplazar todo" de 200 coincidencias se revierte con un único Ctrl+Z.
        doc.BeginUpdate();
        try
        {
            // De la última a la primera: los offsets vienen calculados contra el texto
            // original, así que editar de atrás hacia adelante no corre a las que faltan.
            for (var i = replacements.Count - 1; i >= 0; i--)
            {
                var r = replacements[i];
                if (r.Offset < 0 || r.Length < 0 || r.Offset + r.Length > doc.TextLength) continue;

                doc.Replace(r.Offset, r.Length, r.Text);
                applied++;
            }
        }
        finally
        {
            doc.EndUpdate();
        }

        return applied;
    }

    /// <summary>Selecciona el tramo y hace scroll hasta su línea, sin robarle el foco al
    /// formulario de búsqueda — el usuario sigue tipeando en la ventana flotante.</summary>
    private void SelectAndReveal(int offset, int length)
    {
        var doc = TextEditor.Document;
        if (doc is null || offset < 0 || length < 0 || offset + length > doc.TextLength) return;

        TextEditor.Select(offset, length);

        var location = doc.GetLocation(offset);
        TextEditor.ScrollTo(location.Line, location.Column);
    }

    // ─── Clipboard image paste ────────────────────────────────────────────────

    /// <summary>
    /// Saves the clipboard image to {vault}/attachments/ and inserts a
    /// Markdown image reference at the current caret position.
    /// </summary>
    private void PasteClipboardImage()
    {
        var vaultRoot = App.FileService?.VaultRoot;
        if (string.IsNullOrEmpty(vaultRoot) || !Directory.Exists(vaultRoot))
        {
            MessageBox.Show(
                "Abrí un vault primero para poder pegar imágenes.",
                "Sin vault abierto", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var image = Clipboard.GetImage();
        if (image is null) return;

        // Create attachments folder if needed.
        var attachDir = Path.Combine(vaultRoot, "attachments");
        Directory.CreateDirectory(attachDir);

        // Generate unique filename with timestamp.
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName  = $"screenshot_{timestamp}.png";
        var fullPath  = Path.Combine(attachDir, fileName);

        // Avoid overwrite if pasting multiple times in the same second.
        var counter = 1;
        while (File.Exists(fullPath))
        {
            fileName = $"screenshot_{timestamp}_{counter}.png";
            fullPath = Path.Combine(attachDir, fileName);
            counter++;
        }

        // Encode and save as PNG.
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(fullPath))
        {
            encoder.Save(stream);
        }

        // Insert Markdown image reference at caret.
        var relativePath = $"attachments/{fileName}";
        var markdown     = $"![screenshot]({relativePath})";
        var editor       = TextEditor;
        editor.Document.Insert(editor.CaretOffset, markdown);
        // Note: Document.Insert already advances the caret past the inserted text.
        editor.Focus();
    }

    // ─── Drag & Drop ─────────────────────────────────────────────────────────

    private void EditorView_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void EditorView_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        _vm?.HandleDroppedFiles(files);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void SetEditorText(string text)
    {
        if (TextEditor.Text != text)
            TextEditor.Text = text;
    }

    private void UpdateCaret()
    {
        if (_vm is null) return;
        var loc        = TextEditor.Document.GetLocation(TextEditor.CaretOffset);
        _vm.CurrentLine   = loc.Line;
        _vm.CurrentColumn = loc.Column;
    }

    private void UpdateSyntaxHighlighting()
    {
        UpdateSpellCheckEnabled();

        if (_vm is null || string.IsNullOrEmpty(_vm.CurrentFilePath))
        {
            TextEditor.SyntaxHighlighting = null;
            return;
        }

        var ext = Path.GetExtension(_vm.CurrentFilePath).ToLowerInvariant();
        if (ext == ".md" || ext == ".markdown")
        {
            TextEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Markdown");
        }
        else if (ext == ".html" || ext == ".htm")
        {
            TextEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("HTML")
                                            ?? HighlightingManager.Instance.GetDefinition("XML");
        }
        else
        {
            // Source-code files: use AvalonEdit's built-in definition for the extension.
            // Returns null (plain text) for extensions it doesn't ship — the preview still
            // highlights those via highlight.js.
            TextEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);
        }
    }

    private void ApplyFont(EditorGroupViewModel vm)
    {
        // Font is bound from MainViewModel via DynamicResource on the Window.
        // AvalonEdit respects WPF FontFamily/FontSize on the TextEditor itself.
    }

    // ─── Spell checking ───────────────────────────────────────────────────────

    /// <summary>
    /// Enables the colorizer only for prose files (Markdown/plain text) and forces a
    /// re-colorize. HTML and Mermaid source would be all noise. Called on file/tab change.
    /// </summary>
    private void UpdateSpellCheckEnabled()
    {
        if (_spellColorizer is null) return;

        var path = _vm?.CurrentFilePath;
        var ext  = string.IsNullOrEmpty(path) ? "" : Path.GetExtension(path).ToLowerInvariant();
        bool prose = ext is ".md" or ".markdown" or ".txt" or "";

        _spellColorizer.Enabled = prose;
        TextEditor.TextArea.TextView.Redraw();
    }

    /// <summary>
    /// On right-click, resolves the misspelled word under the pointer and, if there is one,
    /// attaches a fresh <see cref="ContextMenu"/> of replacement suggestions that WPF then
    /// opens. When the click is not on a misspelling, the menu is cleared so nothing pops up —
    /// preserving the editor's previous no-context-menu behavior for ordinary text.
    /// </summary>
    private void TextEditor_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Reset first: a stale menu from a previous right-click must never reappear.
        TextEditor.ContextMenu = null;

        var spell = App.SpellCheckService;
        if (spell is not { IsAvailable: true } || _spellColorizer is not { Enabled: true })
            return;

        var pos = TextEditor.GetPositionFromPoint(e.GetPosition(TextEditor));
        if (pos is null) return;

        int    offset   = TextEditor.Document.GetOffset(pos.Value.Line, pos.Value.Column);
        var    line     = TextEditor.Document.GetLineByOffset(offset);
        string lineText = TextEditor.Document.GetText(line);
        int    column   = offset - line.Offset;

        var word = SpellCheckWordResolver.FindMisspelledWordAt(spell, lineText, column);
        if (word is null) return;

        // Move the caret onto the word so the user sees what will be corrected.
        TextEditor.CaretOffset = line.Offset + word.Value.Offset;
        TextEditor.ContextMenu = BuildSuggestionsMenu(spell, line.Offset + word.Value.Offset, word.Value);
    }

    /// <summary>
    /// Builds the suggestions context menu for a misspelled word at
    /// <paramref name="absoluteOffset"/> in the document. Each item replaces the word in
    /// place; when the engine offers nothing, a single disabled "no suggestions" item is shown.
    /// </summary>
    private ContextMenu BuildSuggestionsMenu(
        ISpellCheckService spell, int absoluteOffset, MisspelledWord word)
    {
        var menu        = new ContextMenu();
        var suggestions = spell.Suggest(word.Word);

        if (suggestions.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "(sin sugerencias)", IsEnabled = false });
            return menu;
        }

        foreach (var suggestion in suggestions)
        {
            var item = new MenuItem { Header = suggestion, FontWeight = FontWeights.SemiBold };
            item.Click += (_, _) => ReplaceWord(absoluteOffset, word.Length, suggestion);
            menu.Items.Add(item);
        }

        return menu;
    }

    /// <summary>Replaces the misspelled span with the chosen suggestion, guarding against a stale offset.</summary>
    private void ReplaceWord(int offset, int length, string replacement)
    {
        if (offset < 0 || offset + length > TextEditor.Document.TextLength) return;
        TextEditor.Document.Replace(offset, length, replacement);
        TextEditor.Focus();
    }

    // ─── Internal-link click handling ────────────────────────────────────────

    private static readonly Regex WikiLinkPattern = new(
        @"\[\[([^\]]+)\]\]", RegexOptions.Compiled);
    private static readonly Regex StdLinkPattern  = new(
        @"\[.*?\]\(([^)]+)\)", RegexOptions.Compiled);

    /// <summary>
    /// Detects clicks on <c>[[wikilinks]]</c> and <c>[text](file.md)</c> in the
    /// editor and navigates to the target file.
    /// </summary>
    private async void TextEditor_PreviewMouseLeftButtonDown(
        object sender, MouseButtonEventArgs e)
    {
        if (_vm is null) return;

        var pos = TextEditor.GetPositionFromPoint(e.GetPosition(TextEditor));
        if (pos is null) return;

        var offset   = TextEditor.Document.GetOffset(pos.Value.Line, pos.Value.Column);
        var line     = TextEditor.Document.GetLineByOffset(offset);
        var lineText = TextEditor.Document.GetText(line);
        var col      = offset - line.Offset;

        // Try wikilink [[target]] first.
        string? target = FindLinkTargetAtColumn(lineText, col, WikiLinkPattern);
        if (target is not null)
        {
            // Wikilinks without extension → add .md
            if (!Path.HasExtension(target)) target += ".md";
        }

        // Then standard [text](target).
        target ??= FindLinkTargetAtColumn(lineText, col, StdLinkPattern);

        if (target is null || _vm.CurrentFilePath is null or "") return;

        // Filter out external URLs and images.
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return;

        var imageExts = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg" };
        if (imageExts.Contains(Path.GetExtension(target), StringComparer.OrdinalIgnoreCase))
            return;

        e.Handled = true;

        try
        {
            var resolved = App.FileService!.ResolveInternalLink(target, _vm.CurrentFilePath);
            await _vm.NavigateToLinkAsync(resolved);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Editor link nav failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the capture-group-1 value of <paramref name="pattern"/> when
    /// the column <paramref name="col"/> falls inside a match, else <c>null</c>.
    /// </summary>
    private static string? FindLinkTargetAtColumn(
        string lineText, int col, Regex pattern)
    {
        foreach (Match m in pattern.Matches(lineText))
        {
            if (col >= m.Index && col < m.Index + m.Length)
                return m.Groups[1].Value.Trim();
        }
        return null;
    }

    // ─── Syntax Highlighting ─────────────────────────────────────────────────

    private static void RegisterMarkdownHighlighting()
    {
        if (HighlightingManager.Instance.GetDefinition("Markdown") is not null) return;

        // NOTE: AvalonEdit <Rule> does NOT support ^ / $ anchors — they can match 0 chars
        // and trigger an endless-loop guard. Line-scoped patterns must use <Span> instead.
        // <Span> without multiline="true" ends automatically at line boundary.
        const string xshd = """
            <?xml version="1.0"?>
            <SyntaxDefinition name="Markdown" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Color name="Heading"    foreground="#569CD6" fontWeight="bold"/>
              <Color name="Bold"       fontWeight="bold"/>
              <Color name="Italic"     fontStyle="italic"/>
              <Color name="Code"       foreground="#CE9178"/>
              <Color name="Link"       foreground="#4EC9B0"/>
              <Color name="WikiLink"   foreground="#C586C0" fontWeight="bold"/>
              <Color name="Image"      foreground="#4EC9B0"/>
              <Color name="Blockquote" foreground="#6A9955"/>
              <Color name="Comment"    foreground="#6A9955"/>
              <RuleSet>

                <!-- Headings: Span (no End) → runs to EOL automatically -->
                <Span color="Heading">
                  <Begin>\#{1,6} </Begin>
                </Span>

                <!-- Blockquote: same pattern -->
                <Span color="Blockquote">
                  <Begin>&gt; </Begin>
                </Span>

                <!-- Fenced code blocks (multiline) -->
                <Span color="Code" multiline="true">
                  <Begin>```</Begin>
                  <End>```</End>
                </Span>

                <!-- Inline code -->
                <Span color="Code">
                  <Begin>`</Begin>
                  <End>`</End>
                </Span>

                <!-- Bold (**) — must come before Italic (*) -->
                <Span color="Bold">
                  <Begin>\*\*</Begin>
                  <End>\*\*</End>
                </Span>
                <Span color="Bold">
                  <Begin>__</Begin>
                  <End>__</End>
                </Span>

                <!-- Italic (*) -->
                <Span color="Italic">
                  <Begin>\*</Begin>
                  <End>\*</End>
                </Span>

                <!-- Images — before links so ![...](...) is matched first -->
                <Rule color="Image">!\[.*?\]\(.*?\)</Rule>

                <!-- Wikilinks [[target]] -->
                <Rule color="WikiLink">\[\[[^\]]+\]\]</Rule>

                <!-- Inline links -->
                <Rule color="Link">\[.*?\]\(.*?\)</Rule>

                <!-- HTML comments -->
                <Span color="Comment" multiline="true">
                  <Begin>&lt;!--</Begin>
                  <End>--&gt;</End>
                </Span>

              </RuleSet>
            </SyntaxDefinition>
            """;

        using var reader = new System.Xml.XmlTextReader(
            new System.IO.StringReader(xshd));
        var def = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        HighlightingManager.Instance.RegisterHighlighting("Markdown", [".md", ".markdown"], def);
    }
}
