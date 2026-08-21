using System.IO;
using System.Text;
using System.Threading.Tasks;
using MarkdownVault.Services.Plugins;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Cubre la ROTACIÓN de <see cref="FilePluginLogSink"/>: la decisión de cuándo
/// <c>plugins.log</c> pasa a ser <c>plugins.1.log</c> (umbral de 1 MB, y solo si el
/// archivo ya existe — ver <c>FilePluginLogSink.RotateIfNeeded</c>).
///
/// La decisión está fusionada con I/O real de disco (<c>FileInfo</c>/<c>File.Move</c>)
/// dentro de un método privado, alimentado por un canal asíncrono en segundo plano —
/// no hay forma de aislarla como función pura sin tocar producción (fuera de alcance
/// acá) ni por reflexión (prohibida a propósito en este proyecto). Por eso estas
/// pruebas ejercitan el comportamiento REAL contra archivos temporales, igual que
/// <see cref="FileServiceExternalChangeTests"/> — no hay mocking framework acá, y la
/// única forma honesta de probar esta decisión es dejarla suceder de verdad. Casos
/// portados desde <c>verify_progress.py</c> (<c>should_rotate</c>).
/// </summary>
public class PluginLogSinkRotationTests : IDisposable
{
    private const long OneMegabyte = 1_048_576;

    // Mismo criterio que Utf8NoBom en FilePluginLogSink: sin BOM para que el largo en
    // bytes del archivo semilla coincida exactamente con la cantidad de caracteres
    // (todos ASCII acá), y así se pueda fijar el tamaño de disparo con precisión.
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _dir;
    private readonly string _logPath;
    private readonly string _backupPath;

    public PluginLogSinkRotationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"mvlogsink_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _logPath = Path.Combine(_dir, "plugins.log");
        _backupPath = Path.Combine(_dir, "plugins.1.log");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Siembra <c>plugins.log</c> con exactamente <paramref name="length"/> bytes,
    /// con <paramref name="marker"/> al principio para poder distinguir el contenido VIEJO
    /// del nuevo después de escribir.</summary>
    private void SeedLog(long length, string marker)
    {
        var filler = new string('a', (int)length - marker.Length);
        File.WriteAllText(_logPath, marker + filler, Utf8NoBom);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return true;
            await Task.Delay(50);
        }
        return predicate();
    }

    /// <summary>
    /// Lee permitiendo que el sink siga escribiendo. <c>File.ReadAllText</c> abre con
    /// <c>FileShare.Read</c>, o sea que mientras lee BLOQUEA la escritura; con polling
    /// cada 50 ms sobre un archivo de 1 MB eso hacía fallar al sink cinco veces
    /// seguidas y se apagaba solo. El test estaba rompiendo lo que medía.
    /// </summary>
    private static string ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task First_write_ever_creates_the_log_without_rotating()
    {
        // should_rotate(exists=False, *) -> False: no hay plugins.log todavía.
        using var sink = new FilePluginLogSink(_logPath);

        sink.Write("core.dictado-voz", "primera línea");

        Assert.True(await WaitUntilAsync(
            () => File.Exists(_logPath) && ReadShared(_logPath).Contains("primera línea"),
            TimeSpan.FromSeconds(5)));
        Assert.False(File.Exists(_backupPath));
    }

    [Fact]
    public async Task Write_below_the_1MB_threshold_does_not_rotate()
    {
        // should_rotate(exists=True, length=1_048_575) -> False.
        SeedLog(OneMegabyte - 1, "MARCA-VIEJA;");
        using var sink = new FilePluginLogSink(_logPath);

        sink.Write("core.dictado-voz", "linea nueva");

        Assert.True(await WaitUntilAsync(
            () => File.Exists(_logPath) && ReadShared(_logPath).Contains("linea nueva"),
            TimeSpan.FromSeconds(5)));
        Assert.False(File.Exists(_backupPath));
        Assert.Contains("MARCA-VIEJA;", ReadShared(_logPath));   // no se perdió lo anterior
    }

    [Fact]
    public async Task Write_exactly_at_the_1MB_threshold_rotates()
    {
        // should_rotate(exists=True, length=1_048_576) -> True (el umbral es inclusivo).
        SeedLog(OneMegabyte, "MARCA-VIEJA;");
        using var sink = new FilePluginLogSink(_logPath);

        sink.Write("core.dictado-voz", "linea nueva");

        Assert.True(await WaitUntilAsync(() => File.Exists(_backupPath), TimeSpan.FromSeconds(5)));
        Assert.Contains("MARCA-VIEJA;", ReadShared(_backupPath));       // el respaldo tiene lo VIEJO
        Assert.DoesNotContain("MARCA-VIEJA;", ReadShared(_logPath));    // el log activo arranca de nuevo
        Assert.Contains("linea nueva", ReadShared(_logPath));
    }

    [Fact]
    public async Task Write_well_above_the_1MB_threshold_rotates()
    {
        // should_rotate(exists=True, length=5_000_000) -> True.
        SeedLog(5_000_000, "MARCA-VIEJA;");
        using var sink = new FilePluginLogSink(_logPath);

        sink.Write("core.dictado-voz", "linea nueva");

        Assert.True(await WaitUntilAsync(() => File.Exists(_backupPath), TimeSpan.FromSeconds(5)));
        Assert.Contains("MARCA-VIEJA;", ReadShared(_backupPath));
        Assert.DoesNotContain("MARCA-VIEJA;", ReadShared(_logPath));
    }
}
