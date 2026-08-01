using MarkdownVault.PluginSdk;

namespace MarkdownVault.Plugin.Mermaid;

/// <summary>
/// Primer plugin de MarkdownVault. Porta la funcionalidad Mermaid que antes
/// estaba hardcodeada en <c>MarkdownService.WrapInPage</c> a dos PreviewAssets:
/// la librería (en el head) y el script de inicialización (al final del body).
/// </summary>
public sealed class MermaidPlugin : IPlugin
{
    public void Configure(IPluginContext context)
    {
        // 1) Cargar la librería Mermaid en el <head>.
        context.AddPreviewAsset(new PreviewAsset
        {
            Kind      = AssetKind.Script,
            Source    = AssetSource.Url,
            Value     = "https://cdn.jsdelivr.net/npm/mermaid@11.15.0/dist/mermaid.min.js",
            Placement = AssetPlacement.HeadEnd
        });

        // 2) Inicializar Mermaid al cargar el DOM (inline, al final del body).
        context.AddPreviewAsset(new PreviewAsset
        {
            Kind      = AssetKind.Script,
            Source    = AssetSource.Inline,
            Value     = InitScript,
            Placement = AssetPlacement.BodyEnd
        });

        // 3) Dropdown de ejemplos en la toolbar (migrado desde el core). Al
        //    desactivar el plugin, este menú desaparece de la barra.
        context.AddCommandGroup(new PluginCommandGroup
        {
            Id    = "core.mermaid.examples",
            Title = "Mermaid",
            Commands = new[]
            {
                Example("Flowchart (diagrama de flujo)", "flowchart"),
                Example("Sequence (secuencia)",          "sequence"),
                Example("Class (clases)",                "class"),
                Example("State (estados)",               "state"),
                Example("Gantt (cronograma)",            "gantt"),
                Example("Pie (torta)",                   "pie"),
                Example("Mindmap (mapa mental)",         "mindmap"),
                Example("Timeline (línea de tiempo)",    "timeline"),
            }
        });

        context.Log("Mermaid registrado (2 preview assets + 1 dropdown).");
    }

    /// <summary>Crea un comando que inserta el snippet de ejemplo del tipo indicado.</summary>
    private static PluginCommand Example(string title, string kind) => new()
    {
        Id      = "core.mermaid.example." + kind,
        Title   = title,
        Execute = editor => editor.InsertAtCaret($"```mermaid\n{Body(kind)}\n```\n")
    };

    private static string Body(string kind) => kind switch
    {
        "flowchart" => """
            flowchart TD
                A([Inicio]) --> B{"¿Condición?"}
                B -->|Sí| C["Procesar datos"]
                B -->|No| D["Terminar"]
                C --> D
            """,
        "sequence" => """
            sequenceDiagram
                participant U as Usuario
                participant A as App
                participant S as Servidor
                U->>A: Abrir archivo
                A->>S: Pedir datos
                S-->>A: Devolver datos
                A-->>U: Mostrar contenido
            """,
        "class" => """
            classDiagram
                class Nota {
                    +String titulo
                    +String contenido
                    +guardar()
                }
                class Vault {
                    +List~Nota~ notas
                    +abrir()
                }
                Vault "1" o-- "muchas" Nota
            """,
        "state" => """
            stateDiagram-v2
                [*] --> Borrador
                Borrador --> Revision : enviar
                Revision --> Publicado : aprobar
                Revision --> Borrador : rechazar
                Publicado --> [*]
            """,
        "gantt" => """
            gantt
                title Cronograma del proyecto
                dateFormat YYYY-MM-DD
                section Planificación
                Análisis       :done,   a1, 2024-01-01, 5d
                Diseño         :active, a2, after a1, 4d
                section Desarrollo
                Implementación :        a3, after a2, 10d
            """,
        "pie" => """
            pie title Distribución de leads por ramo
                "Auto" : 40
                "Salud" : 30
                "Vida" : 20
                "Viaje" : 10
            """,
        "mindmap" => """
            mindmap
              root((MarkdownVault))
                Editor
                  Formato
                  Atajos
                Vista previa
                  Mermaid
                  Tablas
                Grafo
            """,
        "timeline" => """
            timeline
                title Evolución del proyecto
                2023 : Idea inicial
                2024 : Primer prototipo : Vista de grafo
                2025 : Lanzamiento
            """,
        _ => """
            flowchart LR
                A --> B --> C
            """
    };

    // Idéntico al que vivía en MarkdownService: detecta los bloques
    // `pre code.language-mermaid`, los reemplaza por <div class="mermaid"> y corre Mermaid.
    private const string InitScript = """
        document.addEventListener("DOMContentLoaded", function () {
            var blocks = document.querySelectorAll('pre code.language-mermaid');
            if (blocks.length === 0) return;
            var isDark = document.body.classList.contains('dark');
            mermaid.initialize({
                startOnLoad: false,
                theme: isDark ? 'dark' : 'default',
                securityLevel: 'loose'
            });
            blocks.forEach(function (el) {
                var pre = el.parentElement;
                var div = document.createElement('div');
                div.className = 'mermaid';
                div.textContent = el.textContent;
                pre.parentNode.replaceChild(div, pre);
            });
            mermaid.run({ nodes: document.querySelectorAll('.mermaid') });
        });
        """;
}
