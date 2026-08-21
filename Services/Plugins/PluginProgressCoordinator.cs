using MarkdownVault.PluginSdk;

namespace MarkdownVault.Services.Plugins;

/// <summary>
/// Estado que la interfaz necesita para pintar la barra de progreso. Es un tipo
/// del HOST y solo contiene texto y números: nada definido por un plugin viaja
/// acá, para no clavar el <c>AssemblyLoadContext</c> del plugin (ver §9 de
/// docs/plugins/GUIA-PLUGINS.md).
/// </summary>
/// <param name="Title">Rótulo fijo de la operación ("Dictado de voz").</param>
/// <param name="Message">Paso actual ("Descargando el modelo…").</param>
/// <param name="Percent">0-100, o null si el avance es indeterminado.</param>
/// <param name="OtherCount">Cuántos otros scopes están vivos por detrás de este.</param>
/// <param name="IsCancelling">El usuario ya pidió cancelar y se está esperando que el plugin corte.</param>
public sealed record ProgressSnapshot(
    string Title, string Message, double? Percent, int OtherCount, bool IsCancelling);

/// <summary>
/// Multiplexa los <see cref="IProgressScope"/> de todos los plugins en UN solo
/// indicador visible, y es el único lugar del host que sabe de hilos en este
/// asunto.
///
/// ── Política de concurrencia: PILA (LIFO), no cola ─────────────────────────
/// Puede haber varios scopes vivos a la vez. Se muestra SIEMPRE el más reciente;
/// al cerrarlo, reaparece el anterior. El motivo es que los trabajos largos se
/// ANIDAN por causalidad (la transcripción abre el arranque del motor, que abre
/// la descarga del modelo): con una cola FIFO se mostraría el más viejo y el
/// paso que realmente avanza —el de adentro— nunca se vería, que es exactamente
/// el problema que esta función viene a resolver. Además, el scope más nuevo es
/// el que el usuario acaba de provocar con un clic, así que es el que espera ver.
/// Los que quedan atrás no se ocultan del todo: el snapshot lleva
/// <see cref="ProgressSnapshot.OtherCount"/> y la barra dice "+N en segundo plano".
///
/// ── Hilos ──────────────────────────────────────────────────────────────────
/// <c>Report</c> entra desde hilos de fondo (descargas, procesos externos). El
/// estado vive bajo un lock y la NOTIFICACIÓN sale por <c>_marshal</c>, que en
/// producción es <c>Dispatcher.BeginInvoke</c> (se inyecta en App.xaml.cs, mismo
/// criterio que <see cref="HostServices.StatusSink"/>: "el marshaling al hilo de
/// UI es responsabilidad de quien lo inyecta"). En pruebas se inyecta
/// <c>a =&gt; a()</c> y todo queda síncrono.
///
/// ── Ráfagas ────────────────────────────────────────────────────────────────
/// Una descarga puede reportar cientos de veces por segundo. No se posta un
/// mensaje al Dispatcher por reporte: hay una bandera <c>_postPending</c>: el
/// primer reporte agenda un envío, los siguientes solo actualizan el estado, y
/// cuando el envío corre lee el estado ÚLTIMO. Es fusión de eventos sin timers.
/// </summary>
public sealed class PluginProgressCoordinator
{
    /// <summary>Tope del texto que se acepta en la barra. No es una consola de log.</summary>
    internal const int MaxMessageLength = 160;
    internal const int MaxTitleLength   = 80;

    private readonly Action<Action> _marshal;
    private readonly object         _gate  = new();

    /// <summary>Pila de scopes vivos: el último es el que se muestra.</summary>
    private readonly List<ProgressEntry> _stack = new();

    private bool _postPending;

    /// <summary>Último snapshot ENTREGADO. Solo se toca dentro de <see cref="Flush"/>,
    /// que corre siempre en el hilo del marshal — no necesita lock.</summary>
    private ProgressSnapshot? _lastPosted;

    /// <summary>
    /// Cambió lo que hay que mostrar. <c>null</c> = no queda ningún scope vivo,
    /// ocultar el indicador. Se dispara SIEMPRE en el hilo del marshal.
    /// </summary>
    public event Action<ProgressSnapshot?>? ActiveChanged;

    public PluginProgressCoordinator(Action<Action> marshal) => _marshal = marshal;

    // ─── API para el host ────────────────────────────────────────────────────

    /// <summary>Abre un scope a nombre de <paramref name="owner"/> (el id del plugin).</summary>
    public IProgressScope Begin(string owner, string title)
    {
        var entry = new ProgressEntry(this, owner, Normalize(title, "Trabajando…", MaxTitleLength));
        lock (_gate) _stack.Add(entry);
        Publish();
        return entry;
    }

    /// <summary>
    /// Cierra a la fuerza TODO scope de un plugin, cancelando su token primero.
    /// Lo llama <see cref="PluginManager"/> al desactivar: sin esto, un plugin que
    /// se desactiva mientras descarga dejaría la barra colgada para siempre y —peor—
    /// su trabajo de fondo seguiría corriendo sin señal de corte.
    /// </summary>
    public void CloseAllFor(string owner)
    {
        ProgressEntry[] victims;
        lock (_gate)
            victims = _stack.Where(e => string.Equals(e.Owner, owner, StringComparison.OrdinalIgnoreCase)).ToArray();

        foreach (var v in victims) v.ForceClose();
    }

    /// <summary>Cierra a la fuerza todos los scopes (recarga completa de plugins, cierre de la app).</summary>
    public void CloseAll()
    {
        ProgressEntry[] victims;
        lock (_gate) victims = _stack.ToArray();

        foreach (var v in victims) v.ForceClose();
    }

    /// <summary>
    /// Lo que hace el botón «Cancelar» de la barra. Es de DOS TIEMPOS a propósito:
    ///   1ª pulsación → dispara el <c>CancellationToken</c> y la barra pasa a
    ///      «Cancelando…» (el plugin todavía tiene que enterarse y cortar).
    ///   2ª pulsación → DESCARTA el scope del lado del host, aunque el plugin nunca
    ///      lo haya dispuesto.
    /// Esa segunda pulsación es la salida de emergencia que reemplaza a un timeout:
    /// un temporizador tendría que adivinar cuánto es "demasiado", y se equivocaría
    /// con una descarga de 574 MB en una conexión lenta. El usuario, que está
    /// mirando, no se equivoca.
    /// </summary>
    public void CancelOrDiscardActive()
    {
        ProgressEntry? active;
        lock (_gate) active = _stack.Count > 0 ? _stack[^1] : null;
        if (active is null) return;

        if (active.IsCancellationRequested) active.ForceClose();
        else                                active.RequestCancel();
    }

    /// <summary>Solo para diagnóstico y pruebas: cuántos scopes hay vivos.</summary>
    public int ActiveCount { get { lock (_gate) return _stack.Count; } }

    // ─── Lógica pura (verificada aparte, ver architecture/sdk-canal-de-progreso) ──

    /// <summary>
    /// 0-100 recortado. <c>null</c>, NaN o infinito ⇒ indeterminado. Se recorta en
    /// vez de lanzar: un plugin con un bug de porcentaje merece una barra fea, no
    /// una excepción que se lleve puesta su operación.
    /// </summary>
    internal static double? NormalizePercent(double? percent)
    {
        if (percent is not { } p) return null;
        if (double.IsNaN(p) || double.IsInfinity(p)) return null;
        return Math.Clamp(p, 0.0, 100.0);
    }

    /// <summary>
    /// Texto vacío o en blanco CONSERVA el anterior (para que reportar solo el
    /// porcentaje no borre el mensaje). Se recorta al tope con puntos suspensivos.
    /// </summary>
    internal static string Normalize(string? message, string previous, int maxLength = MaxMessageLength)
    {
        if (string.IsNullOrWhiteSpace(message)) return previous;

        var trimmed = message.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "…";
    }

    // ─── Publicación fusionada ───────────────────────────────────────────────

    internal void Touch() => Publish();

    internal void Remove(ProgressEntry entry)
    {
        lock (_gate) _stack.Remove(entry);
        Publish();
    }

    private void Publish()
    {
        lock (_gate)
        {
            if (_postPending) return;   // ya hay un envío agendado: leerá el estado final
            _postPending = true;
        }

        try { _marshal(Flush); }
        catch
        {
            // El Dispatcher puede estar cerrándose (apagado de la app). Soltar la
            // bandera para no quedar mudo si la app sigue viva.
            lock (_gate) _postPending = false;
        }
    }

    private void Flush()
    {
        ProgressSnapshot? snapshot;
        lock (_gate)
        {
            _postPending = false;
            snapshot     = BuildSnapshotLocked();
        }

        // Sin cambio real, sin notificación: evita repintar la barra 200 veces con
        // el mismo 42 % cuando la descarga reporta más fino que lo que se ve.
        if (snapshot == _lastPosted) return;

        _lastPosted = snapshot;
        ActiveChanged?.Invoke(snapshot);
    }

    private ProgressSnapshot? BuildSnapshotLocked()
    {
        if (_stack.Count == 0) return null;

        var top = _stack[^1];
        return new ProgressSnapshot(
            top.Title, top.Message, top.Percent, _stack.Count - 1, top.IsCancellationRequested);
    }
}

/// <summary>
/// El handle que ve el plugin. Guarda SOLO texto, un porcentaje y un
/// <see cref="CancellationTokenSource"/> — nada de tipos del plugin — así que
/// retenerlo mientras el scope vive no impide descargar el ensamblado.
///
/// Todos los métodos son no-ops silenciosos después de cerrarse (dispuesto por el
/// plugin, descartado por el usuario o barrido al desactivar): un plugin no puede
/// recibir una excepción por reportar tarde.
/// </summary>
internal sealed class ProgressEntry : IProgressScope
{
    private readonly PluginProgressCoordinator _coordinator;
    private readonly CancellationTokenSource   _cts = new();
    private readonly object                    _lock = new();

    private string  _message = "";
    private double? _percent;
    private bool    _closed;

    internal ProgressEntry(PluginProgressCoordinator coordinator, string owner, string title)
    {
        _coordinator = coordinator;
        Owner        = owner;
        Title        = title;
    }

    internal string Owner { get; }
    internal string Title { get; }

    internal string  Message { get { lock (_lock) return _message; } }
    internal double? Percent { get { lock (_lock) return _percent; } }

    // El CTS nunca se dispone (ver Close), así que .Token siempre es seguro de leer.
    public CancellationToken CancellationToken => _cts.Token;

    public bool IsCancellationRequested => _cts.IsCancellationRequested;

    public void Report(double? percent, string message)
    {
        lock (_lock)
        {
            if (_closed) return;
            _percent = PluginProgressCoordinator.NormalizePercent(percent);
            _message = PluginProgressCoordinator.Normalize(message, _message);
        }
        _coordinator.Touch();
    }

    public void Report(string message)
    {
        lock (_lock)
        {
            if (_closed) return;
            _message = PluginProgressCoordinator.Normalize(message, _message);
        }
        _coordinator.Touch();
    }

    /// <summary>
    /// Fin NORMAL de la operación. NO cancela: disponer significa "terminé", no
    /// "abortá" — si cancelara, todo <c>using</c> mataría su propio trabajo al salir.
    /// Idempotente.
    /// </summary>
    public void Dispose() => Close(cancel: false);

    /// <summary>Primera pulsación de «Cancelar»: avisa al plugin y deja la barra visible.</summary>
    internal void RequestCancel()
    {
        try { _cts.Cancel(); } catch (ObjectDisposedException) { /* ya cerrado */ }
        _coordinator.Touch();
    }

    /// <summary>
    /// Cierre a la fuerza desde el host (desactivación del plugin, o segunda
    /// pulsación de «Cancelar»). Cancela Y saca de la pila, sin esperar a que el
    /// plugin coopere.
    /// </summary>
    internal void ForceClose() => Close(cancel: true);

    private void Close(bool cancel)
    {
        lock (_lock)
        {
            if (_closed) return;
            _closed = true;
        }

        if (cancel)
            try { _cts.Cancel(); } catch (ObjectDisposedException) { /* ya cerrado */ }

        // El CTS NO se dispone a propósito: puede haber tokens ENLAZADOS creados por
        // el plugin (CreateLinkedTokenSource) que sigan vivos un instante más, y
        // disponer el padre en ese momento es la clase de carrera que produce
        // ObjectDisposedException en el plugin. Sin timers ni registros externos,
        // dejarlo a la basura es correcto.
        _coordinator.Remove(this);
    }
}
