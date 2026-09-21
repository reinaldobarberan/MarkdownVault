using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MarkdownVault.PluginSdk;
using MarkdownVault.ViewModels;
using MarkdownVault.Views;

namespace MarkdownVault.Services.Plugins;

/// <summary>
/// Dueño de los <see cref="KeyBinding"/> de plugin sobre <c>Window.InputBindings</c> (SDK
/// 1.6.0). Traduce las contribuciones vivas de <see cref="PluginRegistry"/> en bindings
/// reales usando el resolver puro (<see cref="PluginShortcutResolver"/>) y rehace TODO el
/// conjunto cada vez que <see cref="PluginRegistry.Changed"/> se dispara — activar,
/// desactivar o togglear cualquier plugin.
///
/// Cruza deliberadamente de <c>Services.Plugins</c> hacia <c>ViewModels</c>
/// (<see cref="EditorGroupViewModel"/>): es el mismo tipo de excepción de capas que
/// <c>MainViewModel.CompareViewFactory</c>/<c>UnsavedChangesPrompt</c> — código de
/// composición sin XAML, cableado una sola vez en <c>App.xaml.cs</c> (Fase 5, todavía NO
/// hecho: esta clase no la construye nadie todavía), no una dependencia que el resto del
/// namespace <c>Services</c> deba imitar.
///
/// No se construye ni se referencia desde ningún otro lado en este batch — compila como
/// código "todavía no usado", a propósito (design Fase 4 termina acá; la Fase 5 la conecta).
/// </summary>
public sealed class PluginShortcutBinder
{
    private readonly Window                    _window;
    private readonly PluginRegistry            _registry;
    private readonly Func<EditorGroupViewModel> _focusedGroup;
    private readonly IPluginLogSink            _logSink;

    // Lo que ESTE binder puso en Window.InputBindings la vuelta anterior, con su dueño —
    // para poder sacar exactamente eso (y nada más) en el próximo barrido.
    private readonly List<(string Owner, KeyBinding Binding)> _bound = new();

    public PluginShortcutBinder(
        Window window,
        PluginRegistry registry,
        Func<EditorGroupViewModel> focusedGroup,
        IPluginLogSink logSink)
    {
        _window       = window ?? throw new ArgumentNullException(nameof(window));
        _registry     = registry ?? throw new ArgumentNullException(nameof(registry));
        _focusedGroup = focusedGroup ?? throw new ArgumentNullException(nameof(focusedGroup));
        _logSink      = logSink ?? throw new ArgumentNullException(nameof(logSink));

        // Dispatcher.Invoke y NUNCA Dispatcher.BeginInvoke. PluginManager.Deactivate corre,
        // en este orden y SINCRÓNICAMENTE: RemoveByOwner -> RaiseChanged -> Alc.Unload() ->
        // GC.Collect(). RaiseChanged es lo que dispara este evento. Si acá abajo se usara
        // BeginInvoke, Rebind() quedaría ENCOLADO para "más tarde" — y "más tarde" en la
        // práctica es DESPUÉS de que Alc.Unload() ya corrió, con los KeyBinding viejos del
        // plugin todavía colgando de tipos de un ensamblado que se está por descargar. Ese
        // KeyBinding sobreviviente es una referencia que el GC.Collect() de la línea 261 de
        // PluginManager.cs no puede soltar: el ALC queda "zombie" para siempre (nunca lo
        // recolecta), rompiendo el invariante que protege
        // Collectible_context_is_unloaded_after_gc. Con Invoke (sincrónico), Rebind() —
        // limpieza incluida — TERMINA antes de que RaiseChanged() le devuelva el control a
        // Deactivate, o sea antes de que Unload() se llegue a ejecutar. Verificado por
        // inspección del código, no solo por compilación (ver PluginManager.cs:249-263).
        _registry.Changed += () => _window.Dispatcher.Invoke(Rebind);
    }

    /// <summary>
    /// Rehace TODO el conjunto de atajos de plugin desde cero: saca los propios de la
    /// vuelta anterior, arma el reservado vigente, resuelve con
    /// <see cref="PluginShortcutResolver"/> y vuelve a agregar los que ganaron. Barrido
    /// total (no un diff por dueño) — mismo criterio que
    /// <c>EditorGroupViewModel.RebuildPluginToolbar</c> (Clear + reconstruir): con a lo
    /// sumo unas pocas decenas de atajos en juego, la simplicidad de "tirar todo y rearmar"
    /// vale más que un diff incremental, y evita dejar un binding viejo colgado si el ORDEN
    /// de los plugins cambió entre una pasada y la siguiente (ese orden decide quién gana
    /// un choque plugin-contra-plugin).
    /// </summary>
    private void Rebind()
    {
        // ─── 1. Sacar lo que este binder puso la vuelta anterior ───────────────────
        foreach (var (_, binding) in _bound)
            _window.InputBindings.Remove(binding);
        _bound.Clear();

        // ─── 2. Reservado = Window.InputBindings YA SIN los de plugin (recién los
        // sacamos arriba, así ningún binding de plugin de la pasada anterior se
        // auto-reserva) ∪ las tres fuentes declaradas en HostGestures. Se recalcula ACÁ
        // ADENTRO en cada ciclo, nunca se cachea entre llamadas: si mañana
        // MainWindow.xaml suma un <KeyBinding>, este barrido lo ve solo, sin tocar este
        // archivo (design D1).
        var reserved = new List<KeyGesture>();
        foreach (var binding in _window.InputBindings)
            if (binding is KeyBinding { Gesture: KeyGesture kg }) reserved.Add(kg);
        reserved.AddRange(HostGestures.EditorConsumed);
        reserved.AddRange(HostGestures.EditorControlDefaults);
        reserved.AddRange(HostGestures.MenuOnlyClaims);

        // ─── 3. Aplanar los comandos propios de los plugins habilitados: sueltos +
        // los que viven adentro de un PluginCommandGroup (ej. "Medios" del plugin
        // Media) — un atajo vale lo mismo esté el comando solo o agrupado.
        var owned = _registry.OwnedCommands
            .Concat(_registry.OwnedCommandGroups.SelectMany(g =>
                g.Group.Commands.Select(cmd => (Owner: g.Owner, Cmd: cmd))));

        // ─── 4. El resolver (puro) decide quién gana cada gesto. ───────────────────
        var resolutions = PluginShortcutResolver.Resolve(owned, reserved);

        foreach (var resolution in resolutions)
        {
            if (resolution.Status != ShortcutStatus.Bound || resolution.KeyGesture is null)
            {
                // Rechazado por CUALQUIER motivo (reservado, choque contra otro plugin o
                // string malformado): se loguea nombrando plugin + gesto, y el comando
                // sigue registrado y clickeable — ni RemoveByOwner ni AddCommand se tocan.
                _logSink.Write(resolution.Owner,
                    $"Atajo de teclado descartado para el comando '{resolution.CommandId}': {resolution.Reason}");
                continue;
            }

            var command = FindCommand(resolution.Owner, resolution.CommandId);
            if (command is null)
                continue; // No debería pasar: el resolver solo resuelve comandos que le pasamos.

            _bound.Add((resolution.Owner, Bind(resolution.Owner, resolution.CommandId, command, resolution.KeyGesture)));
        }
    }

    /// <summary>
    /// Crea el KeyBinding real para un comando ganador. El <see cref="RelayCommand"/> que lo
    /// respalda resuelve <c>FocusedGroup</c> DENTRO del lambda, en el momento del press —
    /// nunca antes, nunca una sola vez al bindear. Copiar acá el patrón "pinned" del toolbar
    /// (<c>EditorGroupViewModel.CreatePluginEditorContext</c> capturado por click, ver
    /// EditorGroupViewModel.cs:736-760) sería CORRECTO para un clic de mouse sobre un botón
    /// que vive en un pane fijo, pero sería un BUG acá: un atajo de TECLADO no vive en ningún
    /// pane, puede dispararse con cualquiera de los dos con foco, y "fijar" el pane en el
    /// momento de construir el binding lo apuntaría para siempre al pane que estaba enfocado
    /// cuando este método corrió — casi nunca el que el usuario tiene en pantalla al apretar
    /// la tecla. Por eso <c>_focusedGroup()</c> se llama adentro del cuerpo del lambda.
    /// </summary>
    private KeyBinding Bind(string owner, string commandId, PluginCommand command, KeyGesture gesture)
    {
        var invoke = new RelayCommand(() =>
        {
            var group = _focusedGroup();
            if (!group.HasOpenDocument) return; // Mismo gate que el botón equivalente (spec).

            try
            {
                command.Execute(group.CreatePluginEditorContext());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginShortcutBinder] {owner}/{commandId}: {ex}");
            }
        });

        var binding = new KeyBinding(invoke, gesture);
        _window.InputBindings.Add(binding);
        return binding;
    }

    private PluginCommand? FindCommand(string owner, string commandId)
    {
        foreach (var (o, cmd) in _registry.OwnedCommands)
            if (Same(o, owner) && cmd.Id == commandId) return cmd;

        foreach (var (o, group) in _registry.OwnedCommandGroups)
            if (Same(o, owner))
                foreach (var cmd in group.Commands)
                    if (cmd.Id == commandId) return cmd;

        return null;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
