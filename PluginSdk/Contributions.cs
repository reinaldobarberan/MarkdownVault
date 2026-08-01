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
