using CommunityToolkit.Mvvm.Input;
using MarkdownVault.PluginSdk;

namespace MarkdownVault.ViewModels;

/// <summary>
/// Un item de la barra de herramientas aportado por un plugin: o un botón único
/// (<see cref="IsGroup"/> = false) o un menú desplegable con <see cref="Children"/>.
/// </summary>
public sealed class PluginToolbarItemViewModel
{
    public string  Title    { get; private init; } = "";
    public string? Icon     { get; private init; }
    public bool    IsGroup  { get; private init; }
    public IRelayCommand? Command { get; private init; }
    public IReadOnlyList<PluginToolbarItemViewModel>? Children { get; private init; }

    /// <summary>
    /// True si el plugin declaró un <see cref="Icon"/> no vacío. Usado por el
    /// DataTrigger de <c>PluginSingleTemplate</c> (EditorView.xaml) para decidir
    /// si el botón renderiza el glifo (Segoe MDL2 Assets) o el texto del título.
    /// </summary>
    public bool HasIcon => !string.IsNullOrEmpty(Icon);

    /// <summary>Crea un botón único a partir de un <see cref="PluginCommand"/>.</summary>
    public static PluginToolbarItemViewModel Single(PluginCommand command, IEditorContext editor) => new()
    {
        Title   = command.Title,
        Icon    = command.Icon,
        IsGroup = false,
        Command = new RelayCommand(() => SafeExecute(command, editor))
    };

    /// <summary>Crea un menú desplegable a partir de un <see cref="PluginCommandGroup"/>.</summary>
    public static PluginToolbarItemViewModel Group(PluginCommandGroup group, IEditorContext editor) => new()
    {
        Title    = group.Title,
        Icon     = group.Icon,
        IsGroup  = true,
        Children = group.Commands.Select(c => Single(c, editor)).ToList()
    };

    private static void SafeExecute(PluginCommand command, IEditorContext editor)
    {
        // Un comando de plugin que explota no debe tumbar la app.
        try { command.Execute(editor); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[plugin-command:{command.Id}] falló: {ex}");
        }
    }
}

/// <summary>
/// Puente entre <see cref="IEditorContext"/> (SDK) y el <see cref="EditorGroupViewModel"/>.
/// Traduce las operaciones a los eventos que ya maneja la vista del editor.
/// </summary>
internal sealed class EditorContextAdapter : IEditorContext
{
    private readonly EditorGroupViewModel _vm;

    public EditorContextAdapter(EditorGroupViewModel vm) => _vm = vm;

    public string Content      => _vm.Content;
    public string SelectedText => _vm.PluginGetSelectedText();

    public void InsertAtCaret(string text)                 => _vm.PluginInsertAtCaret(text);
    public void WrapSelection(string before, string after) => _vm.PluginWrapSelection(before, after);
    public void ReplaceSelection(string text)              => _vm.PluginReplaceSelection(text);
}
