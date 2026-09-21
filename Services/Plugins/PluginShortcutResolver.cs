using System.Windows.Input;
using MarkdownVault.PluginSdk;

namespace MarkdownVault.Services.Plugins;

/// <summary>Qué pasó al intentar reservar el atajo declarado por un <see cref="PluginCommand"/>.</summary>
public enum ShortcutStatus
{
    /// <summary>Parseó, no choca con nada reservado ni con otro plugin: se ata de verdad.</summary>
    Bound,

    /// <summary>El string no lo pudo convertir <see cref="KeyGestureConverter"/>.</summary>
    Malformed,

    /// <summary>Coincide con un gesto de <c>HostGestures</c> o con un <c>Window.InputBindings</c> vivo.</summary>
    ReservedConflict,

    /// <summary>Coincide con el de OTRO plugin ya resuelto antes (orden de carga = orden de registro).</summary>
    PluginConflict
}

/// <summary>
/// Resultado de resolver UN <see cref="PluginCommand.Shortcut"/>. <see cref="KeyGesture"/> es
/// <c>null</c> salvo en <see cref="ShortcutStatus.Bound"/> — un atajo rechazado nunca expone
/// un gesto usable, ni para bindear ni para mostrar (design D6: "un atajo descartado nunca
/// debe mostrarse").
/// </summary>
public sealed record ShortcutResolution(
    string          Owner,
    string          CommandId,
    string          Requested,
    KeyGesture?     KeyGesture,
    ShortcutStatus  Status,
    string?         Reason);

/// <summary>
/// PURO: sin <c>Window</c>, sin <c>Dispatcher</c>, sin nada que dependa de que exista una UI
/// viva. Recibe los comandos propios de los plugins habilitados (ya aplanados: sueltos +
/// los que viven adentro de un <see cref="PluginCommandGroup"/>) y el conjunto reservado ya
/// armado por el llamador (<c>PluginShortcutBinder</c>), y decide, comando por comando, quién
/// se queda con su gesto.
///
/// Esta pureza es deliberada (design D3): es lo que permite testear el resolver en un
/// proyecto <c>net8.0-windows</c> sin levantar una ventana ni un Dispatcher — construir un
/// <see cref="KeyGesture"/>/<see cref="KeyGestureConverter"/> no necesita ninguno de los dos,
/// solo referenciar el ensamblado WPF.
/// </summary>
public static class PluginShortcutResolver
{
    /// <summary>
    /// Resuelve todos los comandos que declaran un <see cref="PluginCommand.Shortcut"/> no
    /// vacío. Un comando SIN shortcut no genera ninguna entrada en el resultado — ni
    /// Bound-con-null ni ningún otro estado: la compatibilidad hacia atrás (spec) exige que
    /// "no declarar atajo" se comporte exactamente como hoy, sin nada que mostrar ni loggear.
    ///
    /// Orden de <paramref name="ownedCommands"/> = orden de resolución = orden de "quién gana"
    /// en un choque plugin-contra-plugin (design D3: orden de carga de plugins, que es el
    /// orden en que <c>HostPluginContext.AddCommand</c> los fue insertando en el registry). El
    /// llamador es responsable de pasarlos en ese orden; este método no reordena nada.
    /// </summary>
    public static IReadOnlyList<ShortcutResolution> Resolve(
        IEnumerable<(string Owner, PluginCommand Cmd)> ownedCommands,
        IReadOnlyCollection<KeyGesture> reserved)
    {
        var reservedSet = new HashSet<KeyGesture>(reserved, GestureValueComparer.Instance);
        var claimed     = new HashSet<KeyGesture>(GestureValueComparer.Instance);
        var converter   = new KeyGestureConverter();
        var results     = new List<ShortcutResolution>();

        foreach (var (owner, cmd) in ownedCommands)
        {
            var requested = cmd.Shortcut;
            if (string.IsNullOrWhiteSpace(requested))
                continue;

            KeyGesture? gesture;
            try
            {
                // Catch ancho A PROPÓSITO: esto parsea un string que viene de OTRO ensamblado,
                // cargado en su propio AssemblyLoadContext, sin ningún control de formato antes
                // de llegar acá. El spec es categórico — "un shortcut malformado NO DEBE afectar
                // la activación del plugin" — así que ninguna excepción de parseo puede escapar
                // de este método, sea cual sea su tipo exacto (KeyGestureConverter puede lanzar
                // NotSupportedException, FormatException o alguna interna de Enum.Parse según
                // qué parte del string esté rota).
                gesture = converter.ConvertFromInvariantString(requested) as KeyGesture;
            }
            catch (Exception)
            {
                gesture = null;
            }

            if (gesture is null)
            {
                results.Add(new ShortcutResolution(owner, cmd.Id, requested, null,
                    ShortcutStatus.Malformed, $"«{requested}» no es un atajo de teclado válido."));
                continue;
            }

            if (reservedSet.Contains(gesture))
            {
                results.Add(new ShortcutResolution(owner, cmd.Id, requested, null,
                    ShortcutStatus.ReservedConflict, $"«{requested}» está reservado por el host o por el editor."));
                continue;
            }

            if (claimed.Contains(gesture))
            {
                results.Add(new ShortcutResolution(owner, cmd.Id, requested, null,
                    ShortcutStatus.PluginConflict, $"«{requested}» ya lo reclamó otro plugin registrado antes."));
                continue;
            }

            claimed.Add(gesture);
            results.Add(new ShortcutResolution(owner, cmd.Id, requested, gesture, ShortcutStatus.Bound, null));
        }

        return results;
    }

    /// <summary>
    /// KeyGesture no redefine Equals/GetHashCode (identidad de referencia por default), pero
    /// acá hace falta comparar por VALOR (mismo Key + mismos Modifiers): los gestos reservados
    /// que vienen de <c>Window.InputBindings</c> son instancias nuevas en cada barrido, y dos
    /// plugins que piden el mismo string ("Ctrl+Shift+V") producen dos instancias de
    /// KeyGesture distintas que deben tratarse como "el mismo atajo".
    /// </summary>
    private sealed class GestureValueComparer : IEqualityComparer<KeyGesture>
    {
        public static readonly GestureValueComparer Instance = new();

        public bool Equals(KeyGesture? x, KeyGesture? y) =>
            ReferenceEquals(x, y) ||
            (x is not null && y is not null && x.Key == y.Key && x.Modifiers == y.Modifiers);

        public int GetHashCode(KeyGesture obj) => HashCode.Combine(obj.Key, obj.Modifiers);
    }
}
