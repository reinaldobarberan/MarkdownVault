using MarkdownVault.PluginSdk;

namespace MarkdownVault.Plugin.CopyButton;

/// <summary>
/// Plugin de PreviewAsset: agrega un botón "Copiar" en la esquina superior derecha
/// de cada bloque de código de la vista previa. Mismo molde que Highlight/Mermaid:
/// un CSS (head) + un script de init (fin del body). No toca el core.
///
/// Detalle importante: la preview se carga con <c>NavigateToString</c>, cuyo origen
/// es <c>about:blank</c> — NO es un contexto seguro, así que <c>navigator.clipboard</c>
/// puede estar ausente o ser rechazado. Por eso el copiado usa <c>navigator.clipboard</c>
/// cuando está disponible y cae a <c>document.execCommand('copy')</c> en caso contrario.
/// </summary>
public sealed class CopyButtonPlugin : IPlugin
{
    public void Configure(IPluginContext context)
    {
        // 1) Estilos del botón (inline, en el head).
        context.AddPreviewAsset(new PreviewAsset
        {
            Kind      = AssetKind.Style,
            Source    = AssetSource.Inline,
            Value     = Css,
            Placement = AssetPlacement.HeadEnd
        });

        // 2) Init: envuelve cada <pre> y le agrega el botón (inline, fin del body).
        context.AddPreviewAsset(new PreviewAsset
        {
            Kind      = AssetKind.Script,
            Source    = AssetSource.Inline,
            Value     = InitScript,
            Placement = AssetPlacement.BodyEnd
        });

        context.Log("Botón de copiar registrado (2 preview assets).");
    }

    private const string Css = """
        /* El wrapper posiciona el botón sin verse afectado por el scroll interno del <pre>. */
        .mv-code-wrap { position: relative; }
        .mv-code-wrap > .mv-copy-btn {
            position: absolute;
            top: 8px;
            right: 8px;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            padding: 5px;
            color: #57606a;
            background: #ffffff;
            border: 1px solid #d0d7de;
            border-radius: 6px;
            cursor: pointer;
            opacity: 0.55;
            transition: opacity .12s ease, color .12s ease, border-color .12s ease, background .12s ease;
        }
        /* El icono hereda el color del botón (currentColor), así el ✓ se pone verde solo. */
        .mv-code-wrap > .mv-copy-btn svg { display: block; width: 16px; height: 16px; }
        .mv-code-wrap:hover > .mv-copy-btn,
        .mv-code-wrap:focus-within > .mv-copy-btn { opacity: 1; }
        .mv-copy-btn:hover { background: #f3f4f6; }
        .mv-copy-btn.mv-copied { color: #1a7f37; border-color: #1a7f37; }
        .mv-copy-btn.mv-error  { color: #cf222e; border-color: #cf222e; }

        /* Tema oscuro. El fondo del <pre> en oscuro es #161b22; el botón DEBE usar
           otro tono (#21262d) o queda camuflado sobre el bloque. Sube también la
           opacidad de reposo para que se note sin depender del hover. */
        body.dark .mv-code-wrap > .mv-copy-btn {
            color: #c9d1d9; background: #21262d; border-color: #30363d; opacity: 0.75;
        }
        body.dark .mv-copy-btn:hover { background: #30363d; }
        body.dark .mv-copy-btn.mv-copied { color: #3fb950; border-color: #3fb950; }
        body.dark .mv-copy-btn.mv-error  { color: #f85149; border-color: #f85149; }
        """;

    // Idempotente: sobrevive al re-dispatch de DOMContentLoaded que hace el host en las
    // actualizaciones in-place (window.__mvSetBody). Saltea los bloques Mermaid (los
    // reemplaza su propio plugin por un diagrama). El copiado usa clipboard API con
    // fallback a execCommand porque el origen about:blank no es contexto seguro.
    private const string InitScript = """
        document.addEventListener("DOMContentLoaded", function () {
            // Iconos SVG (heredan el color vía currentColor). COPY = dos cuadros
            // superpuestos; CHECK = tilde de confirmación al copiar.
            var COPY_ICON  = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="9" y="9" width="13" height="13" rx="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg>';
            var CHECK_ICON = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"></polyline></svg>';

            document.querySelectorAll('pre').forEach(function (pre) {
                // Ya procesado (evita botones duplicados en re-render).
                if (pre.parentElement && pre.parentElement.classList.contains('mv-code-wrap')) return;

                var code = pre.querySelector('code');
                if (!code) return;
                // Mermaid transforma estos bloques en diagramas: no lleva botón.
                if (code.classList.contains('language-mermaid')) return;

                var wrap = document.createElement('div');
                wrap.className = 'mv-code-wrap';
                pre.parentNode.insertBefore(wrap, pre);
                wrap.appendChild(pre);

                var btn = document.createElement('button');
                btn.type = 'button';
                btn.className = 'mv-copy-btn';
                btn.innerHTML = COPY_ICON;
                btn.title = 'Copiar';
                btn.setAttribute('aria-label', 'Copiar código');
                btn.addEventListener('click', function () {
                    copyText(code.textContent).then(function () {
                        flash(btn, CHECK_ICON, 'mv-copied', 'Copiado');
                    }).catch(function () {
                        flash(btn, COPY_ICON, 'mv-error', 'Error al copiar');
                    });
                });
                wrap.appendChild(btn);
            });

            function flash(btn, iconHtml, cls, title) {
                btn.innerHTML = iconHtml;
                btn.title = title;
                btn.classList.add(cls);
                setTimeout(function () {
                    btn.innerHTML = COPY_ICON;
                    btn.title = 'Copiar';
                    btn.classList.remove(cls);
                }, 1200);
            }

            function copyText(text) {
                if (navigator.clipboard && navigator.clipboard.writeText) {
                    return navigator.clipboard.writeText(text).catch(function () {
                        return legacyCopy(text);
                    });
                }
                return legacyCopy(text);
            }

            function legacyCopy(text) {
                return new Promise(function (resolve, reject) {
                    var ta = document.createElement('textarea');
                    ta.value = text;
                    ta.setAttribute('readonly', '');
                    ta.style.position = 'fixed';
                    ta.style.top = '-9999px';
                    document.body.appendChild(ta);
                    ta.select();
                    var ok = false;
                    try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
                    document.body.removeChild(ta);
                    ok ? resolve() : reject();
                });
            }
        });
        """;
}
