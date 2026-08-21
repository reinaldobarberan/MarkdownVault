namespace MarkdownVault.Services;

/// <summary>
/// Costura entre el formulario de Buscar/Reemplazar y el editor concreto sobre el que
/// opera. La implementa <c>EditorView</c> (envolviendo AvalonEdit); en tests se falsea con
/// un doble en memoria, igual que <see cref="IDialogService"/>.
/// </summary>
/// <remarks>
/// Existe porque el editor está DIVIDIDO en dos paneles (<c>IsSplit</c>): el destino no es
/// "el editor" sino el panel con foco en este instante. Por eso el ViewModel no guarda una
/// instancia sino un resolvedor (<c>Func&lt;IFindReplaceTarget?&gt;</c>) que se vuelve a
/// consultar en CADA operación — si el usuario cambia de panel o de pestaña con la ventana
/// de búsqueda abierta, la próxima búsqueda ya va contra el documento correcto.
/// </remarks>
public interface IFindReplaceTarget
{
    /// <summary>Contenido completo del documento activo.</summary>
    string Text { get; }

    /// <summary>Inicio de la selección; el offset del cursor cuando no hay selección.</summary>
    int SelectionStart { get; }

    /// <summary>Largo de la selección; 0 cuando no hay nada seleccionado.</summary>
    int SelectionLength { get; }

    /// <summary>Selecciona el tramo y lo trae a la vista (scroll hasta esa línea).</summary>
    void SelectAndReveal(int offset, int length);

    /// <summary>Reemplaza un tramo y deja seleccionado el texto recién insertado.</summary>
    void ReplaceAndReveal(int offset, int length, string replacement);

    /// <summary>
    /// Aplica todas las ediciones como UNA sola operación de deshacer. Devuelve cuántas
    /// se aplicaron realmente (descarta las que quedaron fuera de rango).
    /// </summary>
    int ApplyReplacements(IReadOnlyList<TextReplacement> replacements);
}
