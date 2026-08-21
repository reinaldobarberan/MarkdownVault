namespace MarkdownVault.PluginSdk;

// ─── PreviewAsset: inyección de CSS/JS en la página HTML de la vista previa ───

public enum AssetKind      { Style, Script }
public enum AssetSource    { Inline, Url, BundledFile }
public enum AssetPlacement { HeadStart, HeadEnd, BodyEnd }

/// <summary>Un recurso (CSS o JS) que el plugin inyecta en la página de preview.</summary>
public sealed class PreviewAsset
{
    public AssetKind      Kind      { get; init; }
    public AssetSource    Source    { get; init; }

    /// <summary>
    /// El CSS/JS inline, la URL, o —para <see cref="AssetSource.BundledFile"/>— la
    /// ruta relativa a la carpeta del plugin (ej. <c>assets/katex.min.js</c>).
    /// </summary>
    public string         Value     { get; init; } = "";
    public AssetPlacement Placement { get; init; } = AssetPlacement.HeadEnd;
}

// ─── Command: acción contribuida a la toolbar / paleta del editor ─────────────

public sealed class PluginCommand
{
    public string  Id      { get; init; } = "";
    public string  Title   { get; init; } = "";
    public string? Icon    { get; init; }
    public Action<IEditorContext> Execute { get; init; } = _ => { };
}

/// <summary>Un grupo de comandos que la UI renderiza como un menú desplegable.</summary>
public sealed class PluginCommandGroup
{
    public string  Id       { get; init; } = "";
    public string  Title    { get; init; } = "";
    public string? Icon     { get; init; }
    public IReadOnlyList<PluginCommand> Commands { get; init; } = Array.Empty<PluginCommand>();
}

// ─── MarkdownExtension: sintaxis nueva Markdown → HTML (vía Markdig) ───────────

/// <summary>
/// Envuelve una extensión de Markdig sin filtrar el tipo de Markdig al contrato.
/// El host castea el retorno a <c>Markdig.IMarkdownExtension</c> al construir el pipeline.
/// </summary>
public interface IMarkdownContribution
{
    object CreateMarkdigExtension();
}

// ─── ListSetting: una lista editable que dibuja el HOST (SDK 1.4.0) ──────────

/// <summary>
/// Una fila de un <see cref="PluginListSetting"/>. <paramref name="Value"/> es
/// <c>null</c> en las listas de UNA sola columna (ver
/// <see cref="PluginListSetting.ValueLabel"/>).
/// </summary>
public readonly record struct PluginListEntry(string Key, string? Value);

/// <summary>
/// Una lista editable que el HOST renderiza dentro de la ventana de complementos.
/// El plugin DECLARA los datos y las reglas propias; no toca WPF ni declara una
/// ventana. Eso no es un detalle estético: un plugin que declara un tipo
/// <c>Window</c> clava su contexto de carga y pierde la descarga en caliente (el
/// caso conocido de Eisenhower, documentado en docs/plugins/GUIA-PLUGINS.md §9).
/// Con este contrato lo único que cruza la frontera son strings y delegates —
/// exactamente lo mismo que ya cruza con <see cref="PluginCommand"/>, que se
/// descarga sin problemas.
///
/// El host se encarga de TODO lo genérico: agregar, editar, borrar, filtrar,
/// avisar duplicados (sin distinguir mayúsculas) y guardar. El plugin se encarga
/// de lo SUYO: de dónde salen los datos (<see cref="Load"/>), adónde van
/// (<see cref="Save"/>) y qué significan (<see cref="Describe"/>).
/// </summary>
public sealed class PluginListSetting
{
    /// <summary>Id estable dentro del plugin (ej. <c>core.dictado.glosario</c>).</summary>
    public string  Id          { get; init; } = "";

    /// <summary>Rótulo de la lista, tal como lo lee el usuario ("Glosario técnico").</summary>
    public string  Title       { get; init; } = "";

    /// <summary>Una línea explicando para qué sirve la lista. Opcional.</summary>
    public string? Description { get; init; }

    /// <summary>Encabezado de la primera columna (ej. "Término").</summary>
    public string  KeyLabel    { get; init; } = "";

    /// <summary>
    /// Encabezado de la segunda columna. <c>null</c> ⇒ la lista tiene UNA sola
    /// columna y <see cref="PluginListEntry.Value"/> viaja siempre en <c>null</c>.
    /// </summary>
    public string? ValueLabel  { get; init; }

    /// <summary>Lee el estado actual. Lo llama el host al abrir la ventana y al descartar cambios.</summary>
    public Func<IReadOnlyList<PluginListEntry>>   Load { get; init; } = () => Array.Empty<PluginListEntry>();

    /// <summary>
    /// Persiste la lista. La recibe ya normalizada por el host: sin espacios
    /// sobrantes, sin entradas vacías y sin claves repetidas. Se llama en el hilo
    /// de UI, así que tiene que ser corta; si lanza, el host muestra el mensaje
    /// junto a la lista y NO da el guardado por hecho.
    /// </summary>
    public Action<IReadOnlyList<PluginListEntry>> Save { get; init; } = _ => { };

    /// <summary>
    /// Opcional. Devuelve un aviso para mostrar bajo la lista (ej. "42 términos ·
    /// 101 de 224 tokens del prompt inicial (45%) · entran ~41 más"), o
    /// <c>null</c> si no hay nada que decir. Mantiene las reglas PROPIAS del
    /// plugin dentro del plugin: el host no sabe —ni tiene por qué saber— qué es
    /// un token de Whisper.
    ///
    /// Se llama con la lista que el usuario tiene en pantalla, guardada o no, y
    /// se vuelve a llamar en cada cambio. Tiene que ser barata y no lanzar; si
    /// lanza, el host se limita a no mostrar nada.
    /// </summary>
    public Func<IReadOnlyList<PluginListEntry>, string?>? Describe { get; init; }
}

// ─── Panel y eventos de vault (plumbing para fases posteriores) ───────────────

public sealed class PluginPanel
{
    public string Id    { get; init; } = "";
    public string Title { get; init; } = "";
}

public sealed class VaultEvent
{
    public string ChangeType { get; init; } = "";
    public string FullPath   { get; init; } = "";
}
