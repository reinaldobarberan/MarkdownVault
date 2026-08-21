using System.Text;
using System.Text.RegularExpressions;

namespace MarkdownVault.Services;

/// <summary>Una coincidencia dentro del documento: offset absoluto y largo, en caracteres.</summary>
public readonly record struct TextMatch(int Offset, int Length)
{
    /// <summary>Primer offset DESPUÉS de la coincidencia.</summary>
    public int End => Offset + Length;
}

/// <summary>
/// Una edición pendiente de «Reemplazar todo». Se calcula entera ANTES de tocar el
/// documento — así el aplicador puede ir de atrás hacia adelante y ningún offset se
/// corre por culpa de una edición previa.
/// </summary>
public readonly record struct TextReplacement(int Offset, int Length, string Text);

/// <summary>Los tres modificadores clásicos de un buscador.</summary>
/// <param name="MatchCase">Distinguir mayúsculas de minúsculas.</param>
/// <param name="WholeWord">Solo coincidencias que sean una palabra entera.</param>
/// <param name="UseRegex">Interpretar el patrón como expresión regular.</param>
public sealed record TextSearchOptions(
    bool MatchCase = false,
    bool WholeWord = false,
    bool UseRegex  = false);

/// <summary>
/// Motor de Buscar/Reemplazar. Es C# puro sobre un <see cref="string"/>: no conoce
/// AvalonEdit, ni WPF, ni el documento abierto — por eso se puede testear headless.
/// Quien tiene el documento (la vista) recibe offsets y decide qué hacer con ellos.
/// </summary>
/// <remarks>
/// Deliberadamente NO se usa <c>ICSharpCode.AvalonEdit.Search.SearchStrategyFactory</c>:
/// su <c>SearchPanel</c> no tiene reemplazo (verificado sobre el ensamblado de
/// Quicker.AvalonEdit 6.3.1 — solo expone FindNext/FindPrevious), así que la mitad del
/// trabajo había que escribirla igual, y atarla a un <c>TextDocument</c> metería la
/// afinidad de hilo de AvalonEdit dentro de la lógica de negocio.
/// </remarks>
public static class TextSearch
{
    // Techo duro para un patrón regex patológico (catastrophic backtracking) escrito por
    // el usuario. Sin esto, un (a+)+$ sobre una nota larga cuelga el hilo de UI.
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Compila el patrón según las opciones. Devuelve false con un mensaje listo para
    /// mostrar cuando el patrón está vacío o la expresión regular es inválida.
    /// </summary>
    public static bool TryBuild(
        string pattern, TextSearchOptions options, out Regex? regex, out string? error)
    {
        regex = null;
        error = null;

        if (string.IsNullOrEmpty(pattern))
        {
            error = "Escribí el texto que querés buscar.";
            return false;
        }

        var body = options.UseRegex ? pattern : Regex.Escape(pattern);

        // Se usan lookarounds en vez de \b a propósito: \b es un borde ENTRE un carácter
        // de palabra y uno que no lo es, así que "palabra completa" sobre un patrón que
        // empieza o termina en símbolo (ej. "(x)" o "->") nunca coincidiría. Los
        // lookarounds solo exigen que no haya un carácter de palabra pegado al borde.
        if (options.WholeWord) body = @"(?<!\w)(?:" + body + @")(?!\w)";

        var flags = RegexOptions.Multiline;
        if (!options.MatchCase) flags |= RegexOptions.IgnoreCase;

        try
        {
            regex = new Regex(body, flags, MatchTimeout);
            return true;
        }
        catch (ArgumentException ex)
        {
            error = $"Expresión regular inválida: {ex.Message}";
            return false;
        }
    }

    /// <summary>Todas las coincidencias del documento, en orden y sin solaparse.</summary>
    public static IReadOnlyList<TextMatch> FindAll(string text, Regex regex)
    {
        var result = new List<TextMatch>();
        foreach (var m in Enumerate(text, regex)) result.Add(new TextMatch(m.Index, m.Length));
        return result;
    }

    /// <summary>
    /// Primera coincidencia que ARRANCA en <paramref name="from"/> o después. Con
    /// <paramref name="wrap"/> vuelve al principio del documento si no encontró nada.
    /// </summary>
    public static TextMatch? FindNext(string text, Regex regex, int from, bool wrap = true)
    {
        var all = FindAll(text, regex);
        if (all.Count == 0) return null;

        foreach (var m in all)
            if (m.Offset >= from) return m;

        return wrap ? all[0] : null;
    }

    /// <summary>
    /// Última coincidencia que TERMINA en <paramref name="before"/> o antes — así, con la
    /// coincidencia actual seleccionada, «anterior» salta a la de atrás y no se queda
    /// clavada en la misma. Con <paramref name="wrap"/> cae a la última del documento.
    /// </summary>
    public static TextMatch? FindPrevious(string text, Regex regex, int before, bool wrap = true)
    {
        var all = FindAll(text, regex);
        if (all.Count == 0) return null;

        for (var i = all.Count - 1; i >= 0; i--)
            if (all[i].End <= before) return all[i];

        return wrap ? all[^1] : null;
    }

    /// <summary>Posición (base 0) de <paramref name="hit"/> dentro de la lista, o -1.</summary>
    public static int IndexOf(IReadOnlyList<TextMatch> all, TextMatch hit)
    {
        for (var i = 0; i < all.Count; i++)
            if (all[i].Offset == hit.Offset) return i;
        return -1;
    }

    /// <summary>
    /// Reemplazo ya expandido si en <paramref name="offset"/> arranca una coincidencia de
    /// exactamente <paramref name="length"/> caracteres; <c>null</c> si lo que está
    /// seleccionado no es una coincidencia. Es la guarda de «Reemplazar»: sin ella, el
    /// botón pisaría texto arbitrario que el usuario haya seleccionado a mano.
    /// </summary>
    public static string? ReplacementAt(
        string text, Regex regex, int offset, int length, string replacement, bool useRegex)
    {
        if (offset < 0 || length <= 0 || offset + length > text.Length) return null;

        var m = regex.Match(text, offset);
        if (!m.Success || m.Index != offset || m.Length != length) return null;

        return Expand(m, replacement, useRegex);
    }

    /// <summary>
    /// Calcula TODAS las ediciones de «Reemplazar todo» contra el texto original, sin
    /// aplicarlas. Los offsets son los del documento sin tocar: quien las aplique debe ir
    /// de la última a la primera.
    /// </summary>
    public static IReadOnlyList<TextReplacement> BuildReplaceAll(
        string text, Regex regex, string replacement, bool useRegex)
    {
        var edits = new List<TextReplacement>();
        foreach (var m in Enumerate(text, regex))
            edits.Add(new TextReplacement(m.Index, m.Length, Expand(m, replacement, useRegex)));
        return edits;
    }

    /// <summary>
    /// Versión pura de «Reemplazar todo» — devuelve el texto resultante. La usan los
    /// tests; la app aplica <see cref="BuildReplaceAll"/> sobre el documento vivo para no
    /// perder el historial de deshacer ni la posición del cursor.
    /// </summary>
    public static string ReplaceAll(
        string text, Regex regex, string replacement, bool useRegex, out int count)
    {
        var edits = BuildReplaceAll(text, regex, replacement, useRegex);
        count = edits.Count;
        if (count == 0) return text;

        var sb   = new StringBuilder(text.Length);
        var read = 0;
        foreach (var e in edits)
        {
            sb.Append(text, read, e.Offset - read);
            sb.Append(e.Text);
            read = e.Offset + e.Length;
        }
        sb.Append(text, read, text.Length - read);
        return sb.ToString();
    }

    // ─── Internos ────────────────────────────────────────────────────────────

    /// <summary>
    /// Recorre las coincidencias salteando las de largo cero. Un patrón como <c>a*</c> o
    /// <c>^</c> coincide con la cadena vacía en cada posición: sin este salto explícito el
    /// recorrido no avanzaría nunca, y una coincidencia vacía tampoco es algo que se pueda
    /// seleccionar ni reemplazar.
    /// </summary>
    private static IEnumerable<Match> Enumerate(string text, Regex regex)
    {
        var from = 0;
        while (from <= text.Length)
        {
            var m = regex.Match(text, from);
            if (!m.Success) yield break;

            if (m.Length == 0) { from = m.Index + 1; continue; }

            yield return m;
            from = m.Index + m.Length;
        }
    }

    /// <summary>
    /// En modo regex se expanden los grupos (<c>$1</c>, <c>$&amp;</c>…); en modo texto
    /// plano el reemplazo es literal, incluidos los <c>$</c> que el usuario haya tipeado.
    /// </summary>
    private static string Expand(Match m, string replacement, bool useRegex) =>
        useRegex ? m.Result(replacement) : replacement;
}
