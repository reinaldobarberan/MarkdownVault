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
using Markdig.Renderers.Html;
using Markdig.Syntax;
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

    // ─── Posición de lectura ──────────────────────────────────────────────────
    // Dueño ÚNICO del scroll vertical programático de este panel: restaurar al cambiar de
    // pestaña y revelar una línea (ancla / Buscar) comparten mecanismo porque comparten causa
    // raíz — el árbol de alturas de AvalonEdit está frío después de reemplazar el documento.
    // Ver EditorScrollRestorer para el mecanismo completo.
    private readonly EditorScrollRestorer _scrollRestorer;

    public EditorView()
    {
        InitializeComponent();
        _scrollRestorer = new EditorScrollRestorer(TextEditor);
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
            _vm.ParagraphLinkRequested -= Vm_ParagraphLinkRequested;
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
        _vm.ParagraphLinkRequested += Vm_ParagraphLinkRequested;
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

    /// <summary>
    /// Guarda la posición de lectura y el cursor en la pestaña que sale, antes del cambio.
    ///
    /// El ancla se guarda en LÍNEA del documento, no en píxeles: ver
    /// <see cref="EditorScrollRestorer"/> para por qué el píxel no se puede restaurar con
    /// <c>WordWrap</c>. Y cuando el editor no se puede medir (panel colapsado por el modo de
    /// vista, o todavía sin dibujar) <c>Capture</c> devuelve <c>null</c> y NO se pisa lo
    /// guardado — escribir "arriba de todo" ahí sería perder la posición a mano.
    /// </summary>
    private void OnActiveTabSaving(OpenTab? tab)
    {
        if (tab is null) return;

        if (_scrollRestorer.Capture() is { } anchor)
            tab.ScrollAnchor = anchor;

        tab.CaretOffset = TextEditor.CaretOffset;
    }

    /// <summary>Restores the editor state when switching to a new tab.</summary>
    private void OnActiveTabChanged(OpenTab? tab)
    {
        _updatingFromVm = true;

        // Un cambio de pestaña nuevo cancela cualquier asentamiento en vuelo: nunca puede haber
        // dos restauraciones peleando por el mismo viewport.
        _scrollRestorer.Cancel();

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

                // El cursor NO lleva la posición de lectura: el setter de TextEditor.Text lo
                // manda a 0 en cada swap y quien lee con la rueda del mouse nunca lo movió. La
                // posición la lleva el ancla, y sola.
                _scrollRestorer.Restore(tab.ScrollAnchor);
            }
            catch { /* ignore if offset is stale */ }

            // Runs AFTER the caret/scroll restore above (design decision #5) — a plain tab
            // switch never has a pending anchor, so this is a no-op for it, and when one IS
            // pending it always wins the ordering race because it's the last thing this tick
            // does.
            ConsumePendingAnchorIfAny();
        }, System.Windows.Threading.DispatcherPriority.Loaded);

        _updatingFromVm = false;
    }

    // ─── Anchor navigation (link-anchors change) ─────────────────────────────

    /// <summary>
    /// Resolves and reveals the one-shot pending anchor set by
    /// <see cref="EditorGroupViewModel.NavigateToLinkAsync"/> against the note that JUST
    /// finished loading into <see cref="TextEditor"/>. Broken/unsupported anchor (design
    /// decision #11, spec "Broken Anchor Handling"): the note is already open at the top (no
    /// extra work needed — nothing scrolled it anywhere else), and the miss is reported via
    /// <see cref="EditorGroupViewModel.StatusSink"/> so it is never silent and never blocks the
    /// navigation that already happened.
    /// </summary>
    private void ConsumePendingAnchorIfAny()
    {
        if (_vm is null) return;
        if (_vm.ConsumePendingAnchor() is not { } target) return;

        // Must come from MarkdownService.ParsePreviewAst — the SAME pipeline instance that
        // renders the preview — so a heading's resolved id here never drifts from what's on
        // screen (design decision #6).
        var doc  = App.MarkdownService.ParsePreviewAst(TextEditor.Text);
        var line = AnchorLocator.Find(doc, target);

        if (!RevealAstLine(line))
            _vm.StatusSink?.Invoke($"No se encontró el ancla «{AnchorDisplay(target)}» en esta nota.");
    }

    /// <summary>
    /// Convierte una línea del AST (base 0, la ÚNICA coordenada que sobrevive al preprocesado
    /// de wikilinks — ver <see cref="MarkdownService.ParsePreviewAst"/>) en un offset del
    /// documento VIVO y hace scroll hasta ahí. Le pregunta el offset a AvalonEdit en vez de
    /// tomarlo del AST, así que no puede estar corrido por construcción. Devuelve
    /// <c>false</c> cuando no había ancla o la línea no existe en este buffer.
    /// </summary>
    private bool RevealAstLine(int? astLine)
    {
        if (astLine is not { } l) return false;

        var doc = TextEditor.Document;
        if (doc is null || l < 0 || l >= doc.LineCount) return false;

        SelectAndReveal(doc.GetLineByNumber(l + 1).Offset, 0);
        return true;
    }

    /// <summary>
    /// Intra-document anchor (spec "Intra-Document Anchor Navigation"): resolves against the
    /// CURRENT buffer only. No tab switch and no Loaded-tick wait is needed — the content is
    /// already loaded and laid out — so this runs synchronously from the click handler itself,
    /// unlike the cross-note case above. Broken/unsupported anchor reports via StatusSink
    /// exactly like the cross-note case instead of doing nothing.
    /// </summary>
    private void JumpToIntraDocumentAnchor(LinkTarget target)
    {
        if (_vm is null) return;

        var doc  = App.MarkdownService.ParsePreviewAst(TextEditor.Text);
        var line = AnchorLocator.Find(doc, target);

        if (!RevealAstLine(line))
            _vm.StatusSink?.Invoke($"No se encontró el ancla «{AnchorDisplay(target)}» en esta nota.");
    }

    private static string AnchorDisplay(LinkTarget target) =>
        target.Kind == AnchorKind.Block ? $"^{target.Anchor}" : target.Anchor ?? string.Empty;

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

    /// <summary>
    /// Botón de barra: mismo trabajo que el ítem del menú contextual, pero sobre el párrafo del
    /// CURSOR. Reusa <see cref="CopyParagraphLink"/> entero — el botón no duplica ni una línea
    /// de la lógica de marcado, solo elige otro offset.
    /// </summary>
    private void Vm_ParagraphLinkRequested() => CopyParagraphLink(TextEditor.CaretOffset);

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

        // SDK 1.6.0 (atajos de plugin): estas cuatro comparaciones testean contra las
        // MISMAS instancias de KeyGesture que HostGestures.EditorConsumed expone —
        // nunca literales Key/ModifierKeys duplicados. Así, el conjunto "reservado" que
        // arma PluginShortcutBinder y lo que esta rama realmente intercepta son,
        // literalmente, el mismo objeto: no pueden divergir con el tiempo. KeyGesture.
        // Matches ya maneja Key.System (combos con Alt) por su cuenta; ninguno de estos
        // cuatro gestos usa Alt, así que el comportamiento no cambia un bit respecto de
        // la comparación manual anterior.
        if (HostGestures.EditorSave.Matches(this, e))
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

        if (HostGestures.EditorPasteImage.Matches(this, e) && Clipboard.ContainsImage())
        {
            PasteClipboardImage();
            e.Handled = true;
            return;
        }
        if (HostGestures.EditorBold.Matches(this, e))
        { _vm.InsertBoldCommand.Execute(null);   e.Handled = true; return; }
        if (HostGestures.EditorItalic.Matches(this, e))
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
    /// formulario de búsqueda — el usuario sigue tipeando en la ventana flotante. Internal
    /// (design decision #6) so the anchor-navigation code further down in this same class can
    /// reuse it for "scroll to this resolved offset" — the intent is identical, just fed by
    /// <see cref="AnchorLocator"/> instead of Find/Replace.
    ///
    /// El scroll va por <see cref="EditorScrollRestorer"/> y no por <c>ScrollTo(línea, columna)</c>
    /// porque ese montaba sobre el MISMO árbol de alturas frío que rompía la restauración de
    /// pestaña: un salto a un ancla en el fondo de una nota larga también quedaba corto (medido:
    /// aterrizaba en la línea 39 pidiendo la 41). El restaurador vuelve a preguntar hasta que la
    /// línea está de verdad en pantalla, y se corta solo si el usuario toca algo.</summary>
    internal void SelectAndReveal(int offset, int length)
    {
        var doc = TextEditor.Document;
        if (doc is null || offset < 0 || length < 0 || offset + length > doc.TextLength) return;

        TextEditor.Select(offset, length);

        var location = doc.GetLocation(offset);
        _scrollRestorer.RevealLine(location.Line);
    }

    // ─── Clipboard image paste ────────────────────────────────────────────────

    /// <summary>
    /// Codifica la imagen del portapapeles a PNG y delega en el ViewModel, que decide EN QUÉ
    /// vault se guarda. La vista no resuelve rutas: antes leía <c>App.FileService.VaultRoot</c>
    /// —la primera raíz abierta— y con dos vaults abiertos el PNG terminaba en el vault
    /// equivocado.
    /// </summary>
    private void PasteClipboardImage()
    {
        if (_vm is null) return;

        var image = Clipboard.GetImage();
        if (image is null) return;

        byte[] png;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var buffer = new MemoryStream())
        {
            encoder.Save(buffer);
            png = buffer.ToArray();
        }

        var markdown = _vm.SavePastedImage(png);
        if (markdown is null) return;

        var editor = TextEditor;
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
    /// Builds the context menu for a right-click on the editor (design decision #7). Always
    /// resets <see cref="TextEditor.ContextMenu"/> to <c>null</c> first — no XAML-declared menu
    /// could survive that unconditional reset, so the menu built here is the only one that ever
    /// shows, assigned at this method's single exit point. When the click landed on a
    /// misspelling, its suggestions are PREPENDED (plus a separator); "Copiar enlace a este
    /// párrafo" (Phase 5, spec "Marker-Writing Boundary") is always appended, spelling or not —
    /// the old code's early-returns on "no misspelling here" would otherwise have skipped
    /// building a menu at all, which is exactly what this rewrite fixes.
    /// </summary>
    private void TextEditor_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Reset first: a stale menu from a previous right-click must never reappear.
        TextEditor.ContextMenu = null;

        var pos = TextEditor.GetPositionFromPoint(e.GetPosition(TextEditor));
        if (pos is null) return;

        int offset = TextEditor.Document.GetOffset(pos.Value.Line, pos.Value.Column);

        // Caret to the clicked offset FIRST (design decision #7): both the spellcheck
        // suggestions below (so the user sees what will be corrected) and
        // "Copiar enlace a este párrafo" (so FindBlockAtPosition resolves the paragraph under
        // the CLICK, not wherever the caret happened to be before the right-click) depend on it.
        TextEditor.CaretOffset = offset;

        var    line     = TextEditor.Document.GetLineByOffset(offset);
        string lineText = TextEditor.Document.GetText(line);
        int    column   = offset - line.Offset;

        // Spellcheck suggestions are now a SKIP, not an early return: the marker-creation item
        // below must still be appended even when there's no misspelling under the click (or
        // spellcheck is unavailable/disabled).
        var spell = App.SpellCheckService;
        MisspelledWord? word = spell is { IsAvailable: true } && _spellColorizer is { Enabled: true }
            ? SpellCheckWordResolver.FindMisspelledWordAt(spell, lineText, column)
            : null;

        var menu = new ContextMenu();

        if (word is { } w)
        {
            // The word's OWN offset can differ from the raw click offset by a few characters
            // within the same line — move the caret onto the word so the user sees what will
            // be corrected, same as before this rewrite.
            var wordOffset = line.Offset + w.Offset;
            TextEditor.CaretOffset = wordOffset;

            foreach (var item in BuildSuggestionItems(spell!, wordOffset, w))
                menu.Items.Add(item);
            menu.Items.Add(new Separator());
        }

        var copyLinkItem = new MenuItem { Header = "Copiar enlace a este párrafo" };
        copyLinkItem.Click += (_, _) => CopyParagraphLink(offset);
        menu.Items.Add(copyLinkItem);

        TextEditor.ContextMenu = menu;
    }

    /// <summary>
    /// Builds the replacement-suggestion items for a misspelled word at
    /// <paramref name="absoluteOffset"/> in the document. Each item replaces the word in place;
    /// when the engine offers nothing, a single disabled "no suggestions" item is yielded so the
    /// caller still has something to show instead of an empty suggestions section.
    /// </summary>
    private IEnumerable<MenuItem> BuildSuggestionItems(
        ISpellCheckService spell, int absoluteOffset, MisspelledWord word)
    {
        var suggestions = spell.Suggest(word.Word);

        if (suggestions.Count == 0)
        {
            yield return new MenuItem { Header = "(sin sugerencias)", IsEnabled = false };
            yield break;
        }

        foreach (var suggestion in suggestions)
        {
            var item = new MenuItem { Header = suggestion, FontWeight = FontWeights.SemiBold };
            item.Click += (_, _) => ReplaceWord(absoluteOffset, word.Length, suggestion);
            yield return item;
        }
    }

    /// <summary>Replaces the misspelled span with the chosen suggestion, guarding against a stale offset.</summary>
    private void ReplaceWord(int offset, int length, string replacement)
    {
        if (offset < 0 || offset + length > TextEditor.Document.TextLength) return;
        TextEditor.Document.Replace(offset, length, replacement);
        TextEditor.Focus();
    }

    // ─── Block-marker creation (Phase 5, spec "Marker-Writing Boundary") ─────

    /// <summary>
    /// "Copiar enlace a este párrafo" (design decision #8): writes a new <c>^id</c> marker ONLY
    /// into THIS open, focused buffer — this handler only ever touches
    /// <see cref="TextEditor"/>'s own live document, never a file that isn't open and focused
    /// here, satisfying the spec's marker-writing boundary by construction. Idempotent: a
    /// paragraph that already carries a marker has its existing id reused instead of a second
    /// one being stacked on top.
    /// </summary>
    private void CopyParagraphLink(int offset)
    {
        if (_vm is null) return;

        var doc = App.MarkdownService.ParsePreviewAst(TextEditor.Text);

        // El offset del clic/caret es del documento VIVO; el AST habla del texto REESCRITO por
        // el preprocesado de wikilinks. Se traduce a LÍNEA antes de tocar el AST porque la
        // línea es la única coordenada común a los dos (ver MarkdownService.ParsePreviewAst).
        var line  = TextEditor.Document.GetLineByOffset(offset).LineNumber - 1;
        var block = AnchorLocator.FindParagraphAtLine(doc, line);
        if (block is null)
        {
            _vm.StatusSink?.Invoke("No hay ningún párrafo en esa posición para enlazar.");
            return;
        }

        var existingId = block.TryGetAttributes()?.Id;
        string id;
        if (existingId is not null &&
            existingId.StartsWith(BlockAnchorExtension.IdPrefix, StringComparison.Ordinal))
        {
            id = existingId[BlockAnchorExtension.IdPrefix.Length..];
        }
        else
        {
            var existingIds = App.MarkdownService.GetBlockMarkers(TextEditor.Text)
                .Select(m => m.Id).ToList();
            id = MarkerId.Generate(existingIds);

            // El marcador va al final de la ÚLTIMA línea del párrafo, y esa posición se la
            // pedimos al documento vivo. Antes se usaba `block.Span.End + 1` — un índice de la
            // cadena REESCRITA por el preprocesado de wikilinks — y con un `[[...]]` en el
            // párrafo eso se iba más allá del final del documento:
            //   ArgumentOutOfRangeException: '0 <= offset <= 45 (Parameter 'offset')'.
            // En notas más largas ni siquiera tiraba excepción: metía el `^id` en medio de otro
            // párrafo, en silencio. Ver MarkdownService.ParsePreviewAst para el mecanismo.
            var lastLine = ParagraphLastLiveLine(doc, block);
            TextEditor.Document.Insert(
                TextEditor.Document.GetLineByNumber(lastLine + 1).EndOffset, " ^" + id);
        }

        var noteName = string.IsNullOrEmpty(_vm.CurrentFilePath)
            ? "nota"
            : Path.GetFileNameWithoutExtension(_vm.CurrentFilePath);
        Clipboard.SetText($"[[{noteName}#^{id}]]");
        _vm.StatusSink?.Invoke("Enlace al párrafo copiado al portapapeles.");
    }

    /// <summary>
    /// Adapta el documento vivo de AvalonEdit a la geometría en líneas de
    /// <see cref="AnchorLocator.ParagraphLastLine"/> (pura y testeable). Todo el cálculo vive
    /// allá; acá sólo se leen líneas del buffer real.
    /// </summary>
    private int ParagraphLastLiveLine(MarkdownDocument doc, ParagraphBlock block) =>
        AnchorLocator.ParagraphLastLine(
            doc, block, TextEditor.Document.LineCount,
            line => TextEditor.Document.GetText(TextEditor.Document.GetLineByNumber(line + 1)));

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

        // Try wikilink [[target]] first, then standard [text](target).
        string? target = FindLinkTargetAtColumn(lineText, col, WikiLinkPattern)
            ?? FindLinkTargetAtColumn(lineText, col, StdLinkPattern);

        if (target is null || _vm.CurrentFilePath is null or "") return;

        // Filter out external URLs and images.
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return;

        // LinkTarget is the sole splitter of `|alias` and `#anchor` (design decision #1). The
        // blind ".md if no extension" append that used to run here — BEFORE ResolveInternalLink
        // even saw the target — is gone: it independently created the same junk-file defect for
        // an anchored wikilink (`Nota#Sección` → `Nota#Sección.md`) that FileService's own fix
        // now closes once, for every caller.
        var parsedTarget = LinkTarget.Parse(target);

        // Check the extension on the NOTE half only — Path.GetExtension on the raw target would
        // otherwise treat an anchored image-shaped target's `#anchor` tail as part of the
        // extension (moot for real images today, but wrong is wrong).
        var imageExts = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg" };
        if (imageExts.Contains(Path.GetExtension(parsedTarget.Note), StringComparer.OrdinalIgnoreCase))
            return;

        e.Handled = true;

        // Intra-document anchor (empty note half): the spec requires scrolling THIS buffer,
        // never opening, navigating, or creating a file.
        if (parsedTarget.Note.Length == 0)
        {
            if (parsedTarget.Kind != AnchorKind.None)
                JumpToIntraDocumentAnchor(parsedTarget);
            return;
        }

        try
        {
            var resolved = App.FileService!.ResolveInternalLink(parsedTarget.Note, _vm.CurrentFilePath);

            // Anchor half re-encoded exactly as LinkTarget.Parse produced it (sigil kept for a
            // block anchor) so NavigateToLinkAsync's single `string? anchor` parameter can
            // round-trip it through LinkTarget.Parse again on the other side (design decision
            // #1's single source of truth) instead of a second Kind parameter.
            string? anchorParam = parsedTarget.Kind switch
            {
                AnchorKind.Heading => parsedTarget.Anchor,
                AnchorKind.Block   => "^" + parsedTarget.Anchor,
                _                  => null,
            };

            await _vm.NavigateToLinkAsync(resolved, anchorParam);
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
