using MarkdownVault.Models;

namespace MarkdownVault.ViewModels;

/// <summary>De dónde sale el texto AUTORITATIVO de una pestaña cuando hay que persistirla.</summary>
internal enum TabContentSource
{
    /// <summary>
    /// Es la pestaña ACTIVA del panel: el texto vivo está en el control de AvalonEdit. Ese
    /// control se espeja en <see cref="EditorGroupViewModel.Content"/> en cada pulsación
    /// (<c>EditorView.Editor_TextChanged</c> → <c>_vm.Content = TextEditor.Text</c>), así que
    /// esa propiedad —y no <see cref="OpenTab.Content"/>— es la fuente que hay que escribir.
    /// </summary>
    LiveEditor,

    /// <summary>
    /// Está en SEGUNDO PLANO: ningún control la está mostrando (hay UN solo editor por panel),
    /// así que la única verdad es <see cref="OpenTab.Content"/>. Es la pestaña que un plugin
    /// puede estar llenando en diferido — ver <see cref="PinnedEditorContext"/>.
    /// </summary>
    TabModel
}

/// <summary>Una escritura pendiente: qué pestaña, de dónde salió su texto y cuál es ese texto.</summary>
internal readonly record struct PendingTabSave(OpenTab Tab, TabContentSource Source, string Content)
{
    public string FilePath => Tab.FilePath;
}

/// <summary>
/// Decisión PURA de "qué hay que guardar y de dónde sale el contenido de cada cosa".
///
/// Existe separada del ViewModel a propósito: es la mitad del arreglo que se puede razonar y
/// probar sin WPF, sin timers y sin disco. El resto (escribir el archivo, avisar errores) es
/// I/O y vive en <see cref="EditorGroupViewModel"/>.
///
/// El porqué, que no es teórico: hasta este arreglo el guardado automático persistía SOLO la
/// pestaña activa (<c>AutoSaveAsync</c> ⇒ <c>if (IsDirty &amp;&amp; HasFile) SaveAsync()</c>, y
/// <c>SaveAsync</c> escribe <c>CurrentFilePath</c> con <c>Content</c>: el documento de adelante).
/// Desde que las escrituras de plugin se fijan a la pestaña donde arrancó la acción
/// (<see cref="PinnedEditorContext"/>), un dictado largo puede ir a una pestaña de SEGUNDO
/// PLANO — y ese texto quedaba marcado como modificado pero solo en memoria, sin tocar disco.
/// </summary>
internal static class DirtyTabScanner
{
    /// <summary>
    /// Devuelve las pestañas que hay que persistir, con la fuente correcta de cada una.
    ///
    /// LO DELICADO está en <paramref name="liveContent"/>: para la pestaña activa NO se usa
    /// <see cref="OpenTab.Content"/> sino el contenido vivo del panel. Escribir la fuente
    /// equivocada no "no guarda": guarda texto VIEJO encima del bueno, que es peor que no
    /// guardar nada.
    /// </summary>
    /// <param name="tabs">Las pestañas abiertas del panel.</param>
    /// <param name="activeTab">La pestaña activa de ESE panel (null si no hay ninguna).</param>
    /// <param name="liveContent">El contenido vivo del panel (<c>EditorGroupViewModel.Content</c>).</param>
    internal static List<PendingTabSave> Scan(
        IEnumerable<OpenTab> tabs, OpenTab? activeTab, string liveContent)
    {
        var pending = new List<PendingTabSave>();

        foreach (var tab in tabs)
        {
            if (!tab.IsDirty) continue;

            // Sin ruta en disco no hay a dónde escribir. NO se descarta el problema: el
            // recuento de cambios sin guardar del cierre SÍ la cuenta (ver
            // UnsavedChangesReport.NeverSaved) para que el usuario se entere en vez de que
            // el guardado automático la ignore en silencio para siempre.
            if (string.IsNullOrWhiteSpace(tab.FilePath)) continue;

            var isActive = ReferenceEquals(tab, activeTab);

            pending.Add(new PendingTabSave(
                tab,
                isActive ? TabContentSource.LiveEditor : TabContentSource.TabModel,
                isActive ? liveContent : tab.Content));
        }

        return pending;
    }

    /// <summary>
    /// ¿Se puede bajar la bandera de "sucio" después de persistir <paramref name="writtenContent"/>?
    /// SOLO si el texto de ahora sigue siendo exactamente el que se escribió.
    ///
    /// No es paranoia: entre el <c>await</c> de la escritura y el retorno, el usuario pudo teclear
    /// y —lo que importa acá— el dictado pudo agregar otra frase a una pestaña de segundo plano.
    /// Bajar la bandera igual dejaría ese texto marcado como guardado sin estarlo: el guardado
    /// automático lo saltearía y el aviso de cierre no lo nombraría. Se pierde en silencio, que es
    /// justo el modo de falla que todo este arreglo existe para cerrar. Quedarse sucio de más solo
    /// cuesta una escritura extra en la próxima pasada.
    /// </summary>
    internal static bool CanClearDirty(string currentContent, string writtenContent) =>
        string.Equals(currentContent, writtenContent, StringComparison.Ordinal);
}
