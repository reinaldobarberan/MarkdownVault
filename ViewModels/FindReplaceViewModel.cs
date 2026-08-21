using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkdownVault.Services;

namespace MarkdownVault.ViewModels;

/// <summary>
/// Estado y comandos del formulario de Buscar/Reemplazar. Sobrevive al cierre de la
/// ventana: <c>MainWindow</c> lo crea una sola vez, así F3 sigue buscando el último patrón
/// aunque el formulario esté escondido, y al reabrirlo están los campos como quedaron.
/// </summary>
public partial class FindReplaceViewModel : ObservableObject
{
    // Resolvedor, no instancia: ver el comentario de IFindReplaceTarget — el destino es el
    // panel con foco AHORA, que puede cambiar entre una búsqueda y la siguiente.
    private readonly Func<IFindReplaceTarget?> _resolveTarget;

    public FindReplaceViewModel(Func<IFindReplaceTarget?> resolveTarget) =>
        _resolveTarget = resolveTarget;

    // ─── Estado del formulario ───────────────────────────────────────────────

    [ObservableProperty] private string _searchText  = string.Empty;
    [ObservableProperty] private string _replaceText = string.Empty;

    [ObservableProperty] private bool _matchCase;
    [ObservableProperty] private bool _wholeWord;
    [ObservableProperty] private bool _useRegex;

    /// <summary>Muestra u oculta la fila de reemplazo (Ctrl+F la esconde, Ctrl+H la abre).</summary>
    [ObservableProperty] private bool _showReplace;

    /// <summary>Última respuesta al usuario: cuántas coincidencias, o por qué falló.</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>True cuando <see cref="Status"/> es un error (patrón vacío, regex inválida…).</summary>
    [ObservableProperty] private bool _isError;

    private TextSearchOptions Options => new(MatchCase, WholeWord, UseRegex);

    // Al cambiar cualquier opción el conteo anterior deja de ser cierto: se limpia el
    // estado en vez de dejar un "Coincidencia 3 de 7" que ya no corresponde.
    partial void OnMatchCaseChanged(bool value) => ClearStatus();
    partial void OnWholeWordChanged(bool value) => ClearStatus();
    partial void OnUseRegexChanged(bool value)  => ClearStatus();
    partial void OnSearchTextChanged(string value) => ClearStatus();

    private void ClearStatus()
    {
        Status  = string.Empty;
        IsError = false;
    }

    // ─── Comandos ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void FindNext() => FindAndReport(forward: true);

    [RelayCommand]
    private void FindPrevious() => FindAndReport(forward: false);

    /// <summary>
    /// Reemplaza la coincidencia seleccionada y salta a la siguiente. Si lo que está
    /// seleccionado NO es una coincidencia (o no hay nada seleccionado), no toca el
    /// documento: solo posiciona en la próxima — el segundo clic ya reemplaza.
    /// </summary>
    [RelayCommand]
    private void Replace()
    {
        if (!TryPrepare(out var target, out var regex)) return;

        var expanded = TextSearch.ReplacementAt(
            target.Text, regex,
            target.SelectionStart, target.SelectionLength,
            ReplaceText, UseRegex);

        if (expanded is null) { FindAndReport(forward: true); return; }

        var offset = target.SelectionStart;
        target.ReplaceAndReveal(offset, target.SelectionLength, expanded);

        // El destino dejó seleccionado el texto insertado, así que buscar desde el final de
        // la selección nunca vuelve a caer sobre lo recién escrito (importante cuando el
        // reemplazo contiene al patrón, ej. "log" -> "catalogo").
        var moved = Move(target, regex, forward: true);
        Report(moved
            ? "Reemplazado. Siguiente coincidencia seleccionada."
            : "Reemplazado. No quedan más coincidencias.", isError: false);
    }

    [RelayCommand]
    private void ReplaceAll()
    {
        if (!TryPrepare(out var target, out var regex)) return;

        var edits = TextSearch.BuildReplaceAll(target.Text, regex, ReplaceText, UseRegex);
        if (edits.Count == 0) { Report(NoMatches(), isError: false); return; }

        var applied = target.ApplyReplacements(edits);
        Report(applied == 1
            ? "1 reemplazo realizado."
            : $"{applied} reemplazos realizados.", isError: false);
    }

    // ─── Apertura desde la ventana principal ─────────────────────────────────

    /// <summary>
    /// Prepara el formulario para mostrarse. Si el editor tiene una selección de una sola
    /// línea, la usa como patrón — es lo que hacen Word, VS Code y Obsidian: seleccionás
    /// una palabra, Ctrl+F, y ya está cargada.
    /// </summary>
    public void PrepareForShow(bool showReplace)
    {
        ShowReplace = showReplace;
        ClearStatus();

        var target = _resolveTarget();
        if (target is null || target.SelectionLength <= 0) return;

        var text  = target.Text;
        var start = target.SelectionStart;
        var len   = target.SelectionLength;
        if (start < 0 || start + len > text.Length) return;

        // Solo una selección de UNA línea sirve como patrón: si el usuario venía con medio
        // párrafo marcado, pisar el campo con eso es más molesto que útil.
        var selected = text.Substring(start, len);
        if (selected.Contains('\n') || selected.Contains('\r')) return;

        SearchText = selected;
    }

    /// <summary>True cuando F3 puede repetir la última búsqueda sin abrir el formulario.</summary>
    public bool HasPattern => !string.IsNullOrEmpty(SearchText);

    // ─── Internos ────────────────────────────────────────────────────────────

    /// <summary>Valida destino + patrón, y compila la regex. Reporta el error si falla.</summary>
    private bool TryPrepare(out IFindReplaceTarget target, out Regex regex)
    {
        target = null!;
        regex  = null!;

        var resolved = _resolveTarget();
        if (resolved is null)
        {
            Report("No hay ningún archivo abierto en el editor.", isError: true);
            return false;
        }

        if (!TextSearch.TryBuild(SearchText, Options, out var built, out var error))
        {
            Report(error!, isError: true);
            return false;
        }

        target = resolved;
        regex  = built!;
        return true;
    }

    private void FindAndReport(bool forward)
    {
        if (!TryPrepare(out var target, out var regex)) return;

        if (!Move(target, regex, forward)) { Report(NoMatches(), isError: false); return; }

        var all   = TextSearch.FindAll(target.Text, regex);
        var index = TextSearch.IndexOf(
            all, new TextMatch(target.SelectionStart, target.SelectionLength));

        Report(index >= 0
            ? $"Coincidencia {index + 1} de {all.Count}."
            : $"{all.Count} coincidencia(s).", isError: false);
    }

    /// <summary>Mueve la selección a la coincidencia siguiente/anterior. False si no hay ninguna.</summary>
    private static bool Move(IFindReplaceTarget target, Regex regex, bool forward)
    {
        var text = target.Text;

        // Hacia adelante se arranca DESPUÉS de la selección (si no, "buscar siguiente"
        // volvería a encontrar la misma). Hacia atrás se corta ANTES de donde empieza.
        var hit = forward
            ? TextSearch.FindNext(text, regex, target.SelectionStart + target.SelectionLength)
            : TextSearch.FindPrevious(text, regex, target.SelectionStart);

        if (hit is null) return false;

        target.SelectAndReveal(hit.Value.Offset, hit.Value.Length);
        return true;
    }

    private string NoMatches() => $"Sin coincidencias para «{SearchText}».";

    private void Report(string message, bool isError)
    {
        Status  = message;
        IsError = isError;
    }
}
