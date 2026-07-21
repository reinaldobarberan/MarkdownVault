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
        FilePath = filePath;
    }

    /// <summary>Full path to the file on disk.</summary>
    public string FilePath { get; }

    /// <summary>Short filename for display in the tab.</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>File extension (lowercase, e.g. ".md").</summary>
    public string Extension => Path.GetExtension(FilePath).ToLowerInvariant();

    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private bool   _isDirty;
    [ObservableProperty] private int    _scrollOffset;
    [ObservableProperty] private int    _caretOffset;
    [ObservableProperty] private bool   _isActive;

    /// <summary>Display name shown on the tab: filename + dirty indicator.</summary>
    public string DisplayName => IsDirty ? $"{FileName} •" : FileName;

    partial void OnIsDirtyChanged(bool value) =>
        OnPropertyChanged(nameof(DisplayName));
}
