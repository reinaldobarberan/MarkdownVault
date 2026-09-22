using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MarkdownVault.Models;

/// <summary>
/// Represents a single open file tab in the editor.
/// Stores per-tab state so the single AvalonEdit/WebView2 can swap content.
/// </summary>
public partial class OpenTab : ObservableObject
{
    public OpenTab(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// Full path to the file on disk. Settable so Save-As can re-key the tab's identity
    /// (split-editor path-uniqueness invariant depends on this staying accurate — see
    /// bug #273: before this became settable, Save-As silently left the tab registered
    /// under its OLD path).
    /// </summary>
    [ObservableProperty] private string _filePath;

    /// <summary>Short filename for display in the tab.</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>File extension (lowercase, e.g. ".md").</summary>
    public string Extension => Path.GetExtension(FilePath).ToLowerInvariant();

    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private bool   _isDirty;

    /// <summary>
    /// Posición de lectura del EDITOR, en línea del documento — no en píxeles. El píxel era la
    /// unidad equivocada para este control: con <c>WordWrap</c> el mismo texto ocupa distinta
    /// altura según el ancho del panel y el tamaño de fuente, y encima el árbol de alturas de
    /// AvalonEdit miente al restaurar (ver <see cref="EditorScrollAnchor"/> y
    /// <c>EditorScrollRestorer</c>).
    /// </summary>
    [ObservableProperty] private EditorScrollAnchor _scrollAnchor = EditorScrollAnchor.Top;

    /// <summary>
    /// Posición de lectura de la VISTA PREVIA (<c>window.scrollY</c>). Va separada del ancla del
    /// editor a propósito: son dos documentos distintos —texto fuente contra HTML renderizado—
    /// y no hay correspondencia línea a línea entre ellos.
    /// </summary>
    [ObservableProperty] private double _previewScrollY;

    [ObservableProperty] private int    _caretOffset;
    [ObservableProperty] private bool   _isActive;

    /// <summary>Display name shown on the tab: filename + dirty indicator.</summary>
    public string DisplayName => IsDirty ? $"{FileName} •" : FileName;

    partial void OnFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(Extension));
        OnPropertyChanged(nameof(DisplayName));
    }

    partial void OnIsDirtyChanged(bool value) =>
        OnPropertyChanged(nameof(DisplayName));
}
