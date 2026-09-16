using System.IO;
using System.Text.Json;
using MarkdownVault.Models;

namespace MarkdownVault.Services;

/// <summary>
/// Remembers each vault's graph settings, one JSON file per vault under
/// <c>%AppData%/MarkdownVault/graphs</c>.
///
/// Outside the vault rather than inside it, deliberately. A file living among the notes would show
/// up in the explorer and — worse — the vault's <c>FileSystemWatcher</c> would fire on every write,
/// so dragging a slider would refresh the file tree dozens of times a second.
///
/// Writes are debounced: a slider drag produces a burst of changes and only the last one matters.
/// Call <see cref="Flush"/> when the app closes so a change made in the final moments is not lost.
/// </summary>
public sealed class GraphSettingsService : IDisposable
{
    /// <summary>How long the last change has to sit still before it is written.</summary>
    private static readonly TimeSpan WriteDelay = TimeSpan.FromMilliseconds(400);

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _directory;
    private readonly object _gate = new();   // `Lock` es .NET 9; este proyecto es net8.0
    private readonly System.Threading.Timer _timer;

    private string?        _pendingRoot;
    private GraphSettings? _pending;

    /// <summary>
    /// Creates the service. <paramref name="directory"/> overrides the AppData location — used by
    /// tests to isolate persistence, same as <see cref="SettingsService"/>.
    /// </summary>
    public GraphSettingsService(string? directory = null)
    {
        _directory = directory ?? Path.Combine(AppPaths.Root, "graphs");
        _timer = new System.Threading.Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Loads a vault's settings; returns the calibrated defaults if missing or corrupt.</summary>
    public GraphSettings Load(string vaultRoot)
    {
        if (string.IsNullOrWhiteSpace(vaultRoot)) return GraphSettings.Defaults();

        try
        {
            var path = PathFor(vaultRoot);
            if (!File.Exists(path)) return GraphSettings.Defaults();
            return JsonSerializer.Deserialize<GraphSettings>(File.ReadAllText(path))
                   ?? GraphSettings.Defaults();
        }
        catch
        {
            return GraphSettings.Defaults();
        }
    }

    /// <summary>
    /// Queues a vault's settings to be written shortly. Calling it again before the delay elapses
    /// replaces the pending write rather than adding a second one.
    /// </summary>
    public void Save(string vaultRoot, GraphSettings settings)
    {
        if (string.IsNullOrWhiteSpace(vaultRoot)) return;

        lock (_gate)
        {
            // Switching vaults mid-debounce would otherwise write the new vault's settings under
            // the old vault's name. Get the pending one out first.
            if (_pendingRoot is not null &&
                !_pendingRoot.Equals(vaultRoot, StringComparison.OrdinalIgnoreCase))
                WritePending();

            _pendingRoot = vaultRoot;
            _pending     = settings;
        }

        _timer.Change(WriteDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Writes any queued settings immediately. Safe to call when nothing is pending.</summary>
    public void Flush()
    {
        lock (_gate) WritePending();
    }

    private void WritePending()
    {
        if (_pendingRoot is null || _pending is null) return;

        var root = _pendingRoot;
        var settings = _pending;
        _pendingRoot = null;
        _pending = null;

        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(PathFor(root), JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch (Exception ex)
        {
            // Never let a settings write take the app down: the graph works fine unsaved.
            System.Diagnostics.Debug.WriteLine($"GraphSettingsService write failed: {ex.Message}");
        }
    }

    private string PathFor(string vaultRoot) => Path.Combine(_directory, FileNameFor(vaultRoot));

    /// <summary>
    /// File name for a vault: a readable slice of the folder name so the directory can be made
    /// sense of by hand, plus a hash of the full path so two vaults called "wiki" never collide.
    /// </summary>
    public static string FileNameFor(string vaultRoot)
    {
        var normalised = (vaultRoot ?? string.Empty)
            .Replace('\\', '/')
            .TrimEnd('/')
            .ToLowerInvariant();

        var name = normalised.Length == 0 ? string.Empty : Path.GetFileName(normalised);
        var readable = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')
                                      .Take(32)
                                      .ToArray());
        if (readable.Length == 0) readable = "vault";

        return $"{readable}-{StableHash(normalised):x8}.json";
    }

    /// <summary>
    /// FNV-1a. Deliberately NOT <c>string.GetHashCode</c>: .NET randomises string hashing per
    /// process, so the same vault would map to a different file on every launch and no setting
    /// would ever be found again.
    /// </summary>
    private static uint StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash;
    }

    public void Dispose()
    {
        Flush();
        _timer.Dispose();
    }
}
