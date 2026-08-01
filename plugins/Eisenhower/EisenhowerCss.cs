namespace MarkdownVault.Plugin.Eisenhower;

/// <summary>
/// CSS de la grilla de Eisenhower (2x2, paleta oklch por cuadrante). Dark-aware vía
/// <c>body.dark</c> — misma convención que <c>MarkdownService.WrapInPage</c>, que agrega
/// la clase <c>dark</c> al body cuando el tema activo es oscuro.
/// </summary>
public static class EisenhowerCss
{
    public const string Css = """
        .eisenhower-grid {
            display: grid;
            grid-template-columns: 1fr 1fr;
            grid-template-rows: 1fr 1fr;
            gap: 12px;
            margin: 16px 0;
        }
        .eisenhower-quadrant {
            border-radius: 8px;
            padding: 12px 16px;
            border-left: 4px solid;
        }
        .eisenhower-quadrant__title {
            margin: 0 0 8px 0;
            font-size: 1em;
            font-weight: 600;
        }
        .eisenhower-quadrant__tasks {
            margin: 0;
            padding-left: 1.2em;
        }
        .eisenhower-task { margin: 2px 0; }

        .eisenhower-quadrant--do {
            border-left-color: oklch(0.70 0.17 25);
            background: oklch(0.70 0.17 25 / 0.10);
        }
        .eisenhower-quadrant--plan {
            border-left-color: oklch(0.65 0.15 245);
            background: oklch(0.65 0.15 245 / 0.10);
        }
        .eisenhower-quadrant--delegate {
            border-left-color: oklch(0.80 0.13 85);
            background: oklch(0.80 0.13 85 / 0.10);
        }
        .eisenhower-quadrant--eliminate {
            border-left-color: oklch(0.70 0.02 250);
            background: oklch(0.70 0.02 250 / 0.10);
        }

        .eisenhower-error {
            border-left: 4px solid oklch(0.65 0.20 25);
            background: oklch(0.65 0.20 25 / 0.10);
            border-radius: 8px;
            padding: 12px 16px;
            margin: 16px 0;
        }

        body.dark .eisenhower-quadrant--do        { background: oklch(0.70 0.17 25 / 0.18); }
        body.dark .eisenhower-quadrant--plan      { background: oklch(0.65 0.15 245 / 0.18); }
        body.dark .eisenhower-quadrant--delegate  { background: oklch(0.80 0.13 85 / 0.18); }
        body.dark .eisenhower-quadrant--eliminate { background: oklch(0.70 0.02 250 / 0.18); }
        body.dark .eisenhower-error { background: oklch(0.65 0.20 25 / 0.18); }
        """;
}
