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

    /// <summary>
    /// Crea un botón único a partir de un <see cref="PluginCommand"/>.
    ///
    /// Recibe una FÁBRICA de contextos, no un contexto: la barra se construye una vez (y se
    /// reconstruye solo al activar/desactivar plugins), pero el contexto tiene que nacer en el
    /// instante del clic para poder fijar la pestaña activa DE ESE MOMENTO. Ver
    /// <see cref="PinnedEditorContext"/> — compartir un contexto entre invocaciones es
    /// literalmente el bug de corrupción silenciosa que este parámetro elimina.
    /// </summary>
    /// <param name="canExecute">
    /// Portón opcional, evaluado por WPF para habilitar o no el botón. El host pasa acá «hay
    /// documento abierto»: sin pestaña, un comando de plugin no tiene a dónde escribir y su
    /// invocación termina degradando con un aviso en la barra de estado
    /// (<see cref="PinnedEditorContext"/>). Un botón gris explica eso mucho mejor que uno vivo
    /// que no hace nada. Null ⇒ siempre habilitado, para llamadores que no declaran portón.
    /// </param>
    public static PluginToolbarItemViewModel Single(
        PluginCommand command, Func<IEditorContext> editorFactory, Func<bool>? canExecute = null) => new()
    {
        Title   = command.Title,
        Icon    = command.Icon,
        IsGroup = false,
        Command = canExecute is null
            ? new RelayCommand(() => SafeExecute(command, editorFactory))
            : new RelayCommand(() => SafeExecute(command, editorFactory), canExecute)
    };

    /// <summary>Crea un menú desplegable a partir de un <see cref="PluginCommandGroup"/>.</summary>
    public static PluginToolbarItemViewModel Group(
        PluginCommandGroup group, Func<IEditorContext> editorFactory, Func<bool>? canExecute = null) => new()
    {
        Title    = group.Title,
        Icon     = group.Icon,
        IsGroup  = true,
        Children = group.Commands.Select(c => Single(c, editorFactory, canExecute)).ToList()
    };

    /// <summary>
    /// Fuerza a WPF a reconsultar el <c>CanExecute</c> de este item y de sus hijos. Hace falta
    /// porque <see cref="RelayCommand"/> no observa nada: si nadie avisa, el botón se queda con
    /// el estado que tenía cuando se construyó la barra.
    /// </summary>
    internal void NotifyCanExecuteChanged()
    {
        Command?.NotifyCanExecuteChanged();
        if (Children is null) return;
        foreach (var child in Children) child.NotifyCanExecuteChanged();
    }

    private static void SafeExecute(PluginCommand command, Func<IEditorContext> editorFactory)
    {
        // Un comando de plugin que explota no debe tumbar la app.
        try { command.Execute(editorFactory()); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[plugin-command:{command.Id}] falló: {ex}");
        }
    }
}
