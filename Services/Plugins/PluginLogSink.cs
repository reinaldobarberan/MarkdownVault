using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Channels;

namespace MarkdownVault.Services.Plugins;

/// <summary>
/// Destino de las líneas que un plugin escribe con <c>IPluginContext.Log</c>.
/// Se inyecta en composición (App.xaml.cs) para que las pruebas no escriban en
/// el disco del usuario.
/// </summary>
public interface IPluginLogSink
{
    void Write(string pluginId, string message);
}

/// <summary>Tira todo. Es el default de <see cref="PluginManager"/> en pruebas.</summary>
public sealed class NullPluginLogSink : IPluginLogSink
{
    public static readonly IPluginLogSink Instance = new NullPluginLogSink();
    private NullPluginLogSink() { }
    public void Write(string pluginId, string message) { }
}

/// <summary>
/// Escribe el log de plugins a <c>%AppData%/MarkdownVault/logs/plugins.log</c>.
///
/// El problema que resuelve: hasta el SDK 1.2.0, <c>IPluginContext.Log</c> iba
/// SOLO a <c>Debug.WriteLine</c> — invisible sin un depurador enganchado y, peor,
/// eliminado por completo en Release (<c>[Conditional("DEBUG")]</c>). Todo el
/// diagnóstico de todos los plugins caía en un pozo. <c>Debug.WriteLine</c> se
/// conserva (sigue siendo cómodo con el depurador abierto); esto se SUMA.
///
/// Cuatro decisiones, con su porqué:
///
/// 1. <b>No bloqueante y acotado.</b> Las líneas entran a un
///    <see cref="Channel"/> ACOTADO con <c>DropWrite</c>: <c>Write</c> jamás
///    espera al disco y jamás hace crecer la memoria sin techo. Si un plugin
///    entra en un bucle de log, se pierden líneas — que es infinitamente mejor
///    que frenarle el hilo o comerse la RAM. Un solo hilo consumidor drena en
///    lotes: una apertura de archivo por ráfaga, no una por línea.
///
/// 2. <b>Tolerante a fallos, hacia adentro.</b> <c>Write</c> nunca lanza. Si el
///    disco falla (permisos, disco lleno, archivo tomado por otro proceso), se
///    cuentan los fallos y tras <see cref="MaxConsecutiveFailures"/> seguidos el
///    sumidero se apaga solo. Un log roto NO puede romper un plugin.
///
/// 3. <b>Rotación simple: 1 MB y un solo respaldo.</b> Al pasar el MB,
///    <c>plugins.log</c> pasa a ser <c>plugins.1.log</c> (pisando el anterior) y
///    se arranca de nuevo. Techo duro ≈ 2 MB. No hace falta más: esto es para
///    leer "qué pasó recién", no para auditoría histórica.
///
/// 4. <b>Marca de tiempo local + id del plugin en cada línea.</b> Local, no UTC:
///    el que lee esto es el usuario comparando contra "recién me pasó", no un
///    sistema correlacionando husos horarios.
/// </summary>
public sealed class FilePluginLogSink : IPluginLogSink, IDisposable
{
    private const long MaxBytes                = 1_048_576;   // 1 MB
    private const int  QueueCapacity           = 2048;
    private const int  MaxConsecutiveFailures  = 5;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string           _path;
    private readonly Channel<string>  _queue;

    private int  _consecutiveFailures;
    private bool _disabled;

    /// <summary>Ruta por defecto: <c>%AppData%/MarkdownVault/logs/plugins.log</c>.</summary>
    public static string DefaultPath => Path.Combine(AppPaths.Root, "logs", "plugins.log");

    public FilePluginLogSink(string? path = null)
    {
        _path  = path ?? DefaultPath;
        _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(QueueCapacity)
        {
            // Descartar lo NUEVO y no lo viejo: en una avalancha, el principio de la
            // avalancha es lo que explica qué la causó.
            FullMode     = BoundedChannelFullMode.DropWrite,
            SingleReader = true
        });

        _ = Task.Run(PumpAsync);
    }

    /// <summary>Ruta efectiva del archivo (diagnóstico).</summary>
    public string FilePath => _path;

    public void Write(string pluginId, string message)
    {
        // TryWrite sobre un canal acotado con DropWrite: no espera, no lanza,
        // devuelve false si se descartó. No se comprueba a propósito — no hay nada
        // sensato que hacer con esa información desde el hilo del plugin.
        try
        {
            _queue.Writer.TryWrite(
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{pluginId}] {message}");
        }
        catch { /* el sumidero nunca propaga hacia el plugin */ }
    }

    private async Task PumpAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                var batch = new StringBuilder();
                while (_queue.Reader.TryRead(out var line))
                    batch.Append(line).Append(Environment.NewLine);

                if (batch.Length > 0) Append(batch.ToString());
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FilePluginLogSink] el hilo de escritura murió: {ex}");
        }
    }

    private void Append(string text)
    {
        if (_disabled) return;

        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            RotateIfNeeded();

            // FileShare.ReadWrite, y NO File.AppendAllText: ese abre pidiendo escritura
            // y permitiendo solo lectura compartida, así que cualquiera que TENGA el
            // archivo abierto para leer (el usuario mirándolo en un editor, un `tail`,
            // un test que hace polling) hace fallar la escritura por conflicto de
            // acceso. Con MaxConsecutiveFailures = 5, mirar el log el tiempo suficiente
            // lo APAGA para el resto de la sesión.
            //
            // Un log que se rompe cuando lo leés no sirve: existe justamente para ser
            // leído mientras la app corre. Encontrado por un test que hacía polling
            // sobre un archivo de 1 MB.
            using (var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            using (var writer = new StreamWriter(fs, Utf8NoBom))
            {
                writer.Write(text);
            }

            _consecutiveFailures = 0;
        }
        catch (Exception ex)
        {
            if (++_consecutiveFailures < MaxConsecutiveFailures) return;

            _disabled = true;
            Debug.WriteLine(
                $"[FilePluginLogSink] {MaxConsecutiveFailures} fallos seguidos escribiendo «{_path}»; " +
                $"se apaga el log a archivo para esta sesión. Último error: {ex.Message}");
        }
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < MaxBytes) return;

        // plugins.log → plugins.1.log (pisa el respaldo anterior).
        var backup = Path.ChangeExtension(_path, ".1.log");
        try
        {
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(_path, backup);
        }
        catch
        {
            // Si la rotación falla (archivo tomado), se sigue escribiendo en el mismo
            // archivo: un log un poco más grande de lo previsto es preferible a perder
            // el diagnóstico. El próximo lote reintenta.
        }
    }

    public void Dispose() => _queue.Writer.TryComplete();
}
