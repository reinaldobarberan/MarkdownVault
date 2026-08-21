using System.Windows.Input;

namespace MarkdownVault.Views;

/// <summary>
/// Comandos de Buscar/Reemplazar del shell. Son <see cref="RoutedUICommand"/> y no comandos
/// del ViewModel porque abren y manejan una VENTANA — eso vive en el code-behind, igual que
/// «Administrar vaults» o «Acerca de».
/// </summary>
/// <remarks>
/// Los atajos NO se declaran acá como <c>InputGestures</c>: se atan con
/// <c>&lt;KeyBinding&gt;</c> explícitos en <c>MainWindow.InputBindings</c>, que es como ya
/// se enganchan Ctrl+S, Ctrl+N y compañía en este proyecto. El texto del atajo en el menú
/// se pone a mano con <c>InputGestureText</c>, misma convención que el resto del menú.
/// </remarks>
public static class FindCommands
{
    public static readonly RoutedUICommand Find =
        new("Buscar", nameof(Find), typeof(FindCommands));

    public static readonly RoutedUICommand Replace =
        new("Reemplazar", nameof(Replace), typeof(FindCommands));

    public static readonly RoutedUICommand FindNext =
        new("Buscar siguiente", nameof(FindNext), typeof(FindCommands));

    public static readonly RoutedUICommand FindPrevious =
        new("Buscar anterior", nameof(FindPrevious), typeof(FindCommands));
}
