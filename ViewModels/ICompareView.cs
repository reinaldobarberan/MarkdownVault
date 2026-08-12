using MarkdownVault.Services;

namespace MarkdownVault.ViewModels;

/// <summary>Pedido de copiar al otro archivo: fila del diff, sentido, y si abarca el bloque entero.</summary>
public readonly record struct CompareMergeRequest(int Row, MergeDirection Direction, bool WholeBlock);

/// <summary>
/// Superficie de comparación que el host (una <c>Window</c> WebView2) le presta al
/// <see cref="MainViewModel"/>. Abstraída para que el VM oriente la sesión de merge sin
/// acoplarse al WebView2 y siga siendo unit-testeable con un doble en memoria.
/// </summary>
public interface ICompareView
{
    /// <summary>Pedido de copiar una línea (o bloque) al otro archivo.</summary>
    event Action<CompareMergeRequest> MergeRequested;

    /// <summary>La vista se cerró; el VM debe soltar la sesión.</summary>
    event Action ViewClosed;

    /// <summary>Muestra la comparación con el HTML inicial ya renderizado.</summary>
    void Show(string html, bool isDark, string title);

    /// <summary>Reemplaza el contenido tras aplicar un merge (re-diff).</summary>
    void Reload(string html);
}
