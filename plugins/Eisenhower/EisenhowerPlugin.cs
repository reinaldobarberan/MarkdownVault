using MarkdownVault.PluginSdk;

namespace MarkdownVault.Plugin.Eisenhower;

/// <summary>
/// Plugin Eisenhower: comando "Tareas Eisenhower" (abre la ventana única de gestión,
/// Change 2b — pivote de UX que reemplaza el modal de solo-captura) + extensión Markdown
/// (grilla ```eisenhower, opcional, Batch 4) + CSS de la grilla. <see cref="Configure"/>
/// cablea las tres contribuciones — la lógica pura (JSON, cuadrante, render, alta/baja/
/// toggle) vive en <see cref="TaskStore"/>, no acá.
/// </summary>
public sealed class EisenhowerPlugin : IPlugin
{
    // Capturado en Configure para que Execute (que solo recibe IEditorContext) pueda
    // llegar a Storage/RequestPreviewRefresh/Host — mismo patrón probado en Change 1.
    // Se guarda un IPluginContext del host, nunca la ventana WPF ni nada del plugin.
    private IPluginContext? _ctx;

    public void Configure(IPluginContext context)
    {
        _ctx = context;

        context.AddCommand(new PluginCommand
        {
            Id      = "core.eisenhower.open",
            Title   = "Tareas Eisenhower",
            // Glifo "ViewAll" (Segoe MDL2 Assets): grid de 4 cuadrados 2x2 — calza
            // conceptualmente con la matriz de cuadrantes Eisenhower. El botón de
            // toolbar (PluginSingleTemplate) lo renderiza como icono; Title queda
            // como tooltip.
            Icon    = "\uE8A9",
            Execute = _ => OpenWindow()
        });

        context.AddMarkdownExtension(new EisenhowerContribution(context.Storage));

        context.AddPreviewAsset(new PreviewAsset
        {
            Kind      = AssetKind.Style,
            Source    = AssetSource.Inline,
            Value     = EisenhowerCss.Css,
            Placement = AssetPlacement.HeadEnd
        });

        context.Log("Eisenhower registrado (1 comando + 1 extensión Markdown + CSS).");
    }

    /// <summary>
    /// Handler del comando "Tareas Eisenhower". Crea la ventana única, la muestra
    /// (ShowDialog bloquea hasta que se cierra) y la descarta al salir del método —
    /// nunca se retiene en el plugin (mismo patrón ALC-safe que el modal que reemplazó).
    /// Toda alta/toggle/baja y su persistencia ocurren DENTRO de la ventana (ver
    /// <see cref="EisenhowerWindow"/>); acá solo se pide un refresh del preview al
    /// cerrar, para que el bloque ```eisenhower opcional (si la nota lo tiene) quede
    /// al día con lo que se haya editado en la ventana.
    /// </summary>
    private void OpenWindow()
    {
        var ctx = _ctx!;

        new EisenhowerWindow(ctx.Storage, ctx.Host).ShowDialog();

        ctx.RequestPreviewRefresh();
    }
}
