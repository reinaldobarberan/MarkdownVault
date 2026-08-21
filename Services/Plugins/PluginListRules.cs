using MarkdownVault.PluginSdk;

namespace MarkdownVault.Services.Plugins;

/// <summary>Qué le pasa a una entrada que el usuario está por confirmar.</summary>
public enum ListEntryProblem
{
    /// <summary>Nada: la entrada es válida.</summary>
    None,

    /// <summary>La clave quedó vacía (o solo espacios).</summary>
    Blank,

    /// <summary>Ya existe otra entrada con esa misma clave, sin distinguir mayúsculas.</summary>
    Duplicate
}

/// <summary>
/// Reglas GENÉRICAS de las listas editables que el host dibuja para los plugins
/// (<see cref="PluginListSetting"/>): normalización, detección de duplicados y
/// filtro de búsqueda. Es la parte que NO cambia de un plugin a otro — lo que sí
/// cambia (qué significan los datos) vive en <c>PluginListSetting.Describe</c>,
/// dentro del plugin.
///
/// Lógica PURA a propósito: sin WPF, sin I/O, sin ObservableCollection. El
/// ViewModel (<c>PluginListSettingViewModel</c>) es una cáscara delgada encima de
/// esto, así que todo lo que se puede equivocar se prueba sin levantar una
/// ventana. Verificado además con un port a Python antes de escribir el C#.
///
/// Criterio de comparación: <see cref="StringComparison.OrdinalIgnoreCase"/>,
/// tanto para duplicados como para el filtro. No es arbitrario: es exactamente el
/// mismo comparador con el que <c>TechnicalGlossary</c> deduplica sus términos,
/// así que la lista que el usuario ve en pantalla y la que el plugin termina
/// guardando coinciden. Los acentos SÍ distinguen ("publico" y "público" son dos
/// entradas distintas), que es lo correcto para un glosario de términos.
/// </summary>
public static class PluginListRules
{
    /// <summary>Recorta la clave. Nunca devuelve null.</summary>
    public static string NormalizeKey(string? key) => (key ?? "").Trim();

    /// <summary>
    /// Recorta el valor y colapsa el vacío a <c>null</c>. La distinción importa:
    /// una segunda columna en blanco significa "sin valor", no "el string vacío",
    /// y el plugin recibe siempre la misma forma.
    /// </summary>
    public static string? NormalizeValue(string? value)
    {
        var trimmed = (value ?? "").Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>Normaliza las dos columnas de una entrada.</summary>
    public static PluginListEntry Normalize(PluginListEntry entry) =>
        new(NormalizeKey(entry.Key), NormalizeValue(entry.Value));

    /// <summary>Una clave que después de recortar no tiene nada.</summary>
    public static bool IsBlank(string? key) => NormalizeKey(key).Length == 0;

    /// <summary>
    /// Índice de la primera entrada cuya clave coincide con <paramref name="key"/>
    /// sin distinguir mayúsculas, o -1 si no hay ninguna. <paramref name="exceptIndex"/>
    /// saltea una posición: es lo que permite EDITAR una fila sin que se detecte a
    /// sí misma como duplicado. Una clave vacía nunca es duplicado (para eso está
    /// <see cref="ListEntryProblem.Blank"/>).
    /// </summary>
    public static int IndexOfKey(IReadOnlyList<PluginListEntry> entries, string? key, int exceptIndex = -1)
    {
        var needle = NormalizeKey(key);
        if (needle.Length == 0) return -1;

        for (var i = 0; i < entries.Count; i++)
        {
            if (i == exceptIndex) continue;
            if (string.Equals(NormalizeKey(entries[i].Key), needle, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>Qué le pasa a <paramref name="key"/> dentro de <paramref name="entries"/>.</summary>
    public static ListEntryProblem Validate(IReadOnlyList<PluginListEntry> entries, string? key, int exceptIndex = -1)
    {
        if (IsBlank(key))                                return ListEntryProblem.Blank;
        if (IndexOfKey(entries, key, exceptIndex) >= 0)   return ListEntryProblem.Duplicate;
        return ListEntryProblem.None;
    }

    /// <summary>
    /// El aviso que ve el usuario, en su idioma y con la etiqueta que eligió el
    /// plugin ("Término", "Palabra", …). Null cuando no hay nada que avisar.
    /// </summary>
    public static string? Message(ListEntryProblem problem, string? keyLabel)
    {
        var label = string.IsNullOrWhiteSpace(keyLabel) ? "La entrada" : keyLabel!.Trim();
        return problem switch
        {
            ListEntryProblem.Blank     => $"«{label}» no puede quedar vacío.",
            ListEntryProblem.Duplicate => $"Ya hay una entrada con ese «{label}» (no se distinguen mayúsculas).",
            _                          => null
        };
    }

    /// <summary>
    /// ¿La entrada sobrevive al filtro? Subcadena en CUALQUIERA de las dos
    /// columnas, sin distinguir mayúsculas. Filtro vacío ⇒ pasa todo.
    /// </summary>
    public static bool Matches(PluginListEntry entry, string? filter)
    {
        var needle = (filter ?? "").Trim();
        if (needle.Length == 0) return true;

        return (entry.Key   ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase)
            || (entry.Value ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Las entradas que sobreviven al filtro, en el mismo orden.</summary>
    public static IReadOnlyList<PluginListEntry> Filter(IReadOnlyList<PluginListEntry> entries, string? filter)
    {
        var result = new List<PluginListEntry>();
        foreach (var entry in entries)
            if (Matches(entry, filter))
                result.Add(entry);
        return result;
    }

    /// <summary>
    /// Deja la lista lista para entregarle al plugin: recorta las dos columnas,
    /// descarta las entradas sin clave y se queda con la PRIMERA de cada clave
    /// repetida (conservando su grafía original, igual que
    /// <c>TechnicalGlossary</c>). Se aplica en los dos sentidos —al leer lo que
    /// devuelve <c>Load</c> y al armar lo que recibe <c>Save</c>— para que un
    /// plugin que devuelve basura no ensucie la pantalla ni al revés.
    /// </summary>
    public static IReadOnlyList<PluginListEntry> Sanitize(IEnumerable<PluginListEntry> entries)
    {
        var result = new List<PluginListEntry>();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in entries)
        {
            var entry = Normalize(raw);
            if (entry.Key.Length == 0) continue;
            if (!seen.Add(entry.Key))  continue;
            result.Add(entry);
        }
        return result;
    }
}
