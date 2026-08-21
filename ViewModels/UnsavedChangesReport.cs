using MarkdownVault.Models;

namespace MarkdownVault.ViewModels;

/// <summary>Un documento con cambios sin guardar, tal como se le nombra al usuario al cerrar.</summary>
/// <param name="FileName">Nombre corto, que es lo que el usuario reconoce.</param>
/// <param name="FilePath">Ruta en disco. Vacía ⇒ la pestaña nunca se guardó (ver <see cref="NeverSaved"/>).</param>
/// <param name="PaneNumber">1 o 2 — en qué panel está, para que sepa dónde buscarla.</param>
internal sealed record UnsavedDocument(string FileName, string FilePath, int PaneNumber)
{
    /// <summary>
    /// Nunca llegó a disco, así que NO hay archivo que actualizar: ni el guardado automático
    /// ni «Guardar y salir» pueden salvarla sin preguntar antes dónde ponerla. Se cuenta y se
    /// marca aparte justamente para que no desaparezca del recuento en silencio.
    /// </summary>
    public bool NeverSaved => string.IsNullOrWhiteSpace(FilePath);
}

/// <summary>
/// Decisión PURA de "¿hay que preguntar al cerrar, y qué exactamente se le muestra al usuario?".
///
/// Sin WPF y sin ventanas a propósito: el ciclo de vida de la ventana no se prueba, esto sí.
/// El diálogo (<c>Views/UnsavedChangesDialog</c>) solo pinta lo que este tipo ya decidió.
///
/// El hueco que cierra: <c>MainWindow.Window_Closing</c> guardaba la configuración y cerraba,
/// sin mirar NUNCA si había pestañas modificadas. Cerrar la aplicación de golpe se llevaba
/// puesto todo lo no guardado. Con el guardado automático alcanzando solo a la pestaña activa,
/// un dictado largo hacia una pestaña de segundo plano se perdía entero.
/// </summary>
internal sealed record UnsavedChangesReport(
    IReadOnlyList<UnsavedDocument> Documents,
    bool                           ShowPaneLabels,
    string?                        BusyOperationTitle)
{
    /// <summary>
    /// ÚNICA condición para interrumpir el cierre: que haya algo real que perder. Sin pestañas
    /// sucias no se pregunta NADA, ni siquiera con una operación larga en curso — un diálogo que
    /// aparece siempre se aprende a despachar sin leerlo, y ahí deja de proteger.
    /// </summary>
    public bool ShouldPrompt => Documents.Count > 0;

    public int Count => Documents.Count;

    /// <summary>Hay al menos un documento que ningún guardado automático puede salvar.</summary>
    public bool HasNeverSaved => Documents.Any(d => d.NeverSaved);

    /// <summary>
    /// Recorre los paneles y arma el informe. Los paneles se pasan en orden (panel 1, panel 2)
    /// porque una <see cref="OpenTab"/> puede MIGRAR de panel sin cerrarse y el usuario necesita
    /// saber dónde está la que se le nombra.
    /// </summary>
    /// <param name="panes">Pestañas abiertas de cada panel, en orden.</param>
    /// <param name="busyOperationTitle">
    /// Título del trabajo largo en curso (dictado, transcripción, descarga de modelo) o null.
    /// Sale de la barra de progreso de plugins, que es lo ÚNICO que el host ya sabe hoy sobre
    /// operaciones en vuelo — ver <c>MainViewModel.IsProgressVisible</c>/<c>ProgressTitle</c>.
    /// </param>
    internal static UnsavedChangesReport Build(
        IReadOnlyList<IReadOnlyList<OpenTab>> panes, string? busyOperationTitle)
    {
        var docs = new List<UnsavedDocument>();

        for (int i = 0; i < panes.Count; i++)
        {
            foreach (var tab in panes[i])
            {
                if (!tab.IsDirty) continue;

                // Sin ruta, OpenTab.FileName es Path.GetFileName("") = cadena VACÍA, y la línea
                // quedaría sin nombre — justo el documento que más falta hace nombrar, porque es
                // el único que ningún guardado puede rescatar solo.
                var displayName = string.IsNullOrWhiteSpace(tab.FileName)
                    ? "(documento sin título)"
                    : tab.FileName;

                docs.Add(new UnsavedDocument(displayName, tab.FilePath, PaneNumber: i + 1));
            }
        }

        return new UnsavedChangesReport(
            docs,
            ShowPaneLabels: panes.Count > 1,
            BusyOperationTitle: string.IsNullOrWhiteSpace(busyOperationTitle) ? null : busyOperationTitle);
    }

    /// <summary>
    /// Cuántos son. Decir el número (y abajo los nombres) en vez de "hay cambios sin guardar":
    /// con el genérico el usuario no puede decidir nada, y termina apretando cualquier botón.
    /// </summary>
    public string Headline => Count == 1
        ? "Hay 1 documento con cambios sin guardar:"
        : $"Hay {Count} documentos con cambios sin guardar:";

    /// <summary>Una línea por documento, ya formateada: nombre + panel + marca de "nunca guardado".</summary>
    public IReadOnlyList<string> Lines =>
        Documents.Select(FormatLine).ToList();

    private string FormatLine(UnsavedDocument doc)
    {
        var line = doc.FileName;
        if (ShowPaneLabels) line += $"  (panel {doc.PaneNumber})";
        if (doc.NeverSaved) line += "  — nunca se guardó en disco";
        return line;
    }

    /// <summary>
    /// Aviso de trabajo largo en vuelo, o null. No se inventa nada: si el plugin no abrió un
    /// scope de progreso, el host NO tiene forma de saber que está trabajando y acá no se
    /// simula que sí. Dictado en vivo y transcripción de archivo sí lo abren.
    /// </summary>
    public string? BusyWarning => BusyOperationTitle is null
        ? null
        : $"Además hay una operación en curso: «{BusyOperationTitle}». " +
          "Si cerrás ahora se corta, y lo que le falte escribir se pierde.";

    /// <summary>Aviso extra cuando «Guardar y salir» no alcanza para todo.</summary>
    public string? NeverSavedWarning => HasNeverSaved
        ? "Los documentos marcados nunca se guardaron en disco: hay que elegirles una ubicación " +
          "a mano antes de cerrar."
        : null;
}
