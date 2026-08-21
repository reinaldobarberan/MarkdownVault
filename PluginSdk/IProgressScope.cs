namespace MarkdownVault.PluginSdk;

/// <summary>
/// Canal de progreso para operaciones LARGAS (segundos o minutos): descargas,
/// arranque de un motor externo, transcripciones. Es el complemento de
/// <see cref="IHostServices.ShowStatus"/>, no su reemplazo — ShowStatus sigue
/// siendo el canal correcto para un aviso INSTANTÁNEO ("Guardado 20:14:03",
/// "no se detectó habla", un error); esto es para cuando el usuario tiene que
/// esperar y necesita ver que la aplicación sigue viva.
///
/// Contrato de uso, en una línea:
/// <code>
/// using var progreso = context.Host.BeginProgress("Dictado de voz");
/// progreso.Report(null, "Preparando el motor…");        // indeterminado
/// progreso.Report(42, "Descargando el modelo (574 MB)…"); // 42 %
/// if (progreso.IsCancellationRequested) return;
/// </code>
///
/// Reglas del contrato (las cumple el host, el plugin puede confiar en ellas):
///
/// 1. <b>Ciclo de vida determinista.</b> El indicador aparece con
///    <see cref="IHostServices.BeginProgress"/> y desaparece con
///    <see cref="IDisposable.Dispose"/>. Usá <c>using</c>: si el plugin se olvida
///    de disponer, el indicador queda visible hasta que el usuario lo descarte a
///    mano o hasta que el plugin se desactive (ver regla 5). NO hay temporizador
///    que lo cierre solo — ver §5 de la guía de plugins para el porqué.
///
/// 2. <b>Hilos.</b> <see cref="Report"/> se puede llamar desde CUALQUIER hilo.
///    El traslado al hilo de interfaz lo hace el host. Un plugin NUNCA necesita
///    saber qué es un Dispatcher para reportar progreso. Llamadas muy seguidas se
///    fusionan del lado del host: reportar cada 0,5 % no satura la interfaz.
///
/// 3. <b>Cancelación.</b> <see cref="CancellationToken"/> se dispara cuando el
///    usuario aprieta «Cancelar» en la barra de progreso. Observalo en tus bucles
///    y pasalo a las llamadas asíncronas. <see cref="IDisposable.Dispose"/> NO
///    cancela — disponer significa "terminé", no "abortá".
///
/// 4. <b>Concurrencia.</b> Puede haber varios scopes vivos a la vez (dos plugins,
///    o un plugin con dos trabajos). El host muestra el MÁS RECIENTE y anota
///    cuántos quedan atrás; al disponerlo, vuelve a mostrarse el anterior. Ver la
///    justificación de esta política (pila, no cola) en §5 de la guía.
///
/// 5. <b>Descarga en caliente.</b> Al desactivar un plugin, el host cancela y
///    cierra TODOS los scopes que ese plugin dejó abiertos. Después de eso,
///    <see cref="Report"/> y <see cref="IDisposable.Dispose"/> son no-ops
///    silenciosos: nunca lanzan. El host no retiene ningún tipo definido por el
///    plugin (un scope solo guarda texto y un CancellationTokenSource), así que
///    esto no interfiere con la descarga del ensamblado.
/// </summary>
public interface IProgressScope : IDisposable
{
    /// <summary>
    /// Se dispara cuando el usuario cancela esta operación desde la interfaz.
    /// Es <see cref="System.Threading.CancellationToken.None"/> si el host no
    /// tiene un canal de progreso conectado (ver <see cref="NoOpProgressScope"/>).
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>Atajo de <c>CancellationToken.IsCancellationRequested</c>, para bucles.</summary>
    bool IsCancellationRequested { get; }

    /// <summary>
    /// Informa avance. <paramref name="percent"/> va de 0 a 100 (NO de 0 a 1: el
    /// host recorta al rango y un 0,5 se leería como medio por ciento). Pasar
    /// <c>null</c> —o un valor no finito— pone el indicador en modo INDETERMINADO:
    /// "esto está trabajando pero no se sabe cuánto falta".
    ///
    /// <paramref name="message"/> vacío o en blanco CONSERVA el mensaje anterior,
    /// para que reportar solo el porcentaje no borre el texto. Los mensajes muy
    /// largos se recortan: la barra no es una consola de log (para eso está
    /// <see cref="IPluginContext.Log"/>).
    /// </summary>
    void Report(double? percent, string message);

    /// <summary>
    /// Cambia solo el mensaje y CONSERVA el modo actual (determinado con su
    /// porcentaje, o indeterminado). Distinto de <c>Report(null, mensaje)</c>,
    /// que además fuerza el modo indeterminado.
    /// </summary>
    void Report(string message);
}

/// <summary>
/// Scope que no hace nada. Sirve para dos cosas:
/// (a) el host devuelve esto cuando no hay canal de progreso conectado, para que
///     <see cref="IHostServices.BeginProgress"/> NUNCA devuelva null;
/// (b) un plugin lo usa como valor por defecto en un método auxiliar que acepta
///     un <see cref="IProgressScope"/> pero también tiene que poder correr sin
///     interfaz (por ejemplo, desde una prueba).
/// </summary>
public sealed class NoOpProgressScope : IProgressScope
{
    public static readonly IProgressScope Instance = new NoOpProgressScope();

    private NoOpProgressScope() { }

    public CancellationToken CancellationToken => CancellationToken.None;
    public bool IsCancellationRequested        => false;

    public void Report(double? percent, string message) { }
    public void Report(string message)                  { }
    public void Dispose()                               { }
}
