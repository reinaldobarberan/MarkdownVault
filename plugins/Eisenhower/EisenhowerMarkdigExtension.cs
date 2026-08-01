using System.IO;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using MarkdownVault.PluginSdk;

namespace MarkdownVault.Plugin.Eisenhower;

/// <summary>
/// Extensión de Markdig que registra <see cref="EisenhowerCodeBlockRenderer"/> para
/// interceptar los fenced code blocks con info string <c>eisenhower</c>. No aporta
/// sintaxis nueva al parser (el fence ```eisenhower ya es Markdown estándar) — solo
/// cambia CÓMO se renderiza ese bloque particular a HTML.
/// </summary>
public sealed class EisenhowerMarkdigExtension : IMarkdownExtension
{
    private readonly IPluginStorage _storage;

    public EisenhowerMarkdigExtension(IPluginStorage storage)
    {
        _storage = storage;
    }

    /// <summary>No-op: no hace falta ningún cambio en el parser, solo en el render.</summary>
    public void Setup(MarkdownPipelineBuilder pipeline) { }

    /// <summary>
    /// Inserta nuestro renderer PRIMERO en la cadena (índice 0).
    /// </summary>
    /// <remarks>
    /// DESVIACIÓN VERIFICADA respecto del diseño original: Markdig 1.1.2 despacha por
    /// asignabilidad de TIPO (CodeBlock), no por contenido — <c>Accept</c> es <c>sealed</c>
    /// y no expone un hook por-objeto sobreescribible. Insertar en el índice 0 NO produce
    /// automáticamente un "fallback al siguiente renderer" para los bloques que no son
    /// nuestros: nuestro renderer se convierte en el único candidato para TODO CodeBlock
    /// (incluidos los fenced con otro info string). El narrowing por info-string y el
    /// fallback al <see cref="CodeBlockRenderer"/> estándar viven, por lo tanto, DENTRO de
    /// <see cref="EisenhowerCodeBlockRenderer.Write"/>, verificado empíricamente con un
    /// pipeline Markdig real antes de implementar (ver EisenhowerRenderExtensionTests).
    /// </remarks>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        renderer.ObjectRenderers.Insert(0, new EisenhowerCodeBlockRenderer(_storage));
    }
}

/// <summary>
/// Renderer de code blocks que intercepta los fenced con info <c>eisenhower</c> y los
/// reemplaza por la grilla de Eisenhower; cualquier otro code block se delega, sin
/// cambios, al <see cref="CodeBlockRenderer"/> estándar de Markdig (Highlight/Mermaid,
/// ambos client-side sobre el <c>&lt;pre&gt;&lt;code&gt;</c> resultante, quedan intactos).
/// </summary>
public sealed class EisenhowerCodeBlockRenderer : HtmlObjectRenderer<CodeBlock>
{
    private const string TasksFileName = "tasks.json";

    private readonly IPluginStorage _storage;
    private readonly CodeBlockRenderer _fallback = new();

    public EisenhowerCodeBlockRenderer(IPluginStorage storage)
    {
        _storage = storage;
    }

    protected override void Write(HtmlRenderer renderer, CodeBlock obj)
    {
        if (obj is FencedCodeBlock fenced && fenced.Info == "eisenhower")
        {
            renderer.WriteLine(RenderGrid());
            return;
        }

        _fallback.Write(renderer, obj);
    }

    /// <summary>
    /// Lectura SÍNCRONA de tasks.json, guardada por <see cref="IPluginStorage.Exists"/>.
    /// Deliberado (decisión de diseño): el render de Markdig es síncrono, así que se evita
    /// el contrato async de <see cref="IPluginStorage"/> en esta ruta; RootPath es
    /// contractual y el nombre de archivo es un literal constante (sin traversal).
    /// </summary>
    private string RenderGrid()
    {
        var raw = _storage.Exists(TasksFileName)
            ? File.ReadAllText(Path.Combine(_storage.RootPath, TasksFileName))
            : null;

        var loadResult = TaskStore.Load(raw);
        return TaskStore.RenderGridHtml(loadResult);
    }
}
