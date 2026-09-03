using System.IO;
using MarkdownVault.Models;

namespace MarkdownVault.Services;

/// <summary>
/// A vault-scoped file-system event: <paramref name="Root"/> is the open vault root whose
/// watcher fired it (closed over at watcher-creation time), so subscribers can refresh only
/// the affected section instead of rebuilding everything.
/// </summary>
public record VaultChange(string Root, FileSystemEventArgs Args);

/// <summary>
/// Manages vault I/O across one or more open vault roots: opening/closing roots, building
/// file trees, CRUD operations, and watching for external changes via one
/// <see cref="FileSystemWatcher"/> per root.
/// </summary>
public class FileService : IDisposable
{
    // ─── Root set (copy-on-write) ───────────────────────────────────────────────
    //
    // _vaultRoots is swapped (never mutated in place) under _rootsLock so that readers —
    // GetOwningRoot, GetVaultFiles, VaultRoots itself — can iterate a snapshot without ever
    // taking the lock or seeing a torn collection, even while a watcher thread-pool callback
    // is mid-flight during an AddRoot/RemoveRoot. The watcher dictionary is guarded by the
    // same lock since its membership must stay consistent with the roots list.
    private readonly object _rootsLock = new();
    private List<string> _vaultRoots = [];
    private readonly Dictionary<string, FileSystemWatcher> _watchers =
        new(StringComparer.OrdinalIgnoreCase);

    // Last write-time the app itself produced for a path, used to tell our own
    // saves apart from external edits. Accessed from both the UI thread (writes)
    // and watcher thread-pool threads, so guard every access with _writeLock.
    private readonly object                     _writeLock      = new();
    private readonly Dictionary<string, DateTime> _selfWriteTimes =
        new(StringComparer.OrdinalIgnoreCase);

    // Per-path "I am writing this" grace window. The watcher Changed event runs on a
    // thread-pool thread and can be processed BEFORE the write records its exact stamp
    // (a race that made auto-save look like an external edit and popped the reload
    // prompt while the user was typing). Marking the path around our write absorbs that.
    private readonly Dictionary<string, DateTime> _selfWriteGuardUntil =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan SelfWriteGuard = TimeSpan.FromSeconds(2);

    /// <summary>Every currently open vault root, in open order. Lock-free snapshot read.</summary>
    public IReadOnlyList<string> VaultRoots => _vaultRoots;

    /// <summary>
    /// Legacy single-root accessor, preserved for callers not yet migrated to
    /// <see cref="VaultRoots"/> (Phases 2-7): the first/top open root, or <c>null</c> when
    /// none is open.
    /// </summary>
    public string? VaultRoot => _vaultRoots.Count > 0 ? _vaultRoots[0] : null;

    /// <summary>Raised on the thread pool whenever an open vault root's file system changes.</summary>
    public event EventHandler<VaultChange>? VaultChanged;

    /// <summary>
    /// Raised (on a thread-pool thread) when an open file's <em>content</em> is modified
    /// by an external process — i.e. a watcher Changed event that is not one of the app's
    /// own saves. Subscribers must marshal to the UI thread before touching view state.
    /// Stays path-keyed (not root-keyed): subscribers already filter down to open files.
    /// </summary>
    public event EventHandler<FileSystemEventArgs>? FileChangedExternally;

    // ─── Vault lifecycle ──────────────────────────────────────────────────────

    /// <summary>
    /// Legacy single-root entry point, preserved for callers not yet migrated to
    /// <see cref="AddRoot"/>/<see cref="RemoveRoot"/> (Phase 5). Replaces the entire open-root
    /// set with just <paramref name="path"/>, matching the pre-multi-vault "switch vault"
    /// behavior.
    /// </summary>
    public void OpenVault(string path)
    {
        foreach (var root in _vaultRoots.ToList())
            RemoveRoot(root);
        AddRoot(path);
    }

    /// <summary>
    /// Adds <paramref name="path"/> to the open root set and starts watching it. Idempotent
    /// and case-insensitive: a no-op (no duplicate section, no duplicate watcher) when the
    /// root is already open.
    /// </summary>
    public void AddRoot(string path)
    {
        var normalized = Path.GetFullPath(path);

        lock (_rootsLock)
        {
            if (_vaultRoots.Any(r => string.Equals(r, normalized, StringComparison.OrdinalIgnoreCase)))
                return;

            var updated = new List<string>(_vaultRoots) { normalized };
            _vaultRoots = updated;
            _watchers[normalized] = CreateWatcherForRoot(normalized);
        }
    }

    /// <summary>
    /// Removes <paramref name="path"/> from the open root set and disposes its watcher.
    /// Idempotent: a no-op when the root isn't open. Open tabs from that root are left
    /// untouched by design — only the caller's tree section and watcher go away.
    /// </summary>
    public void RemoveRoot(string path)
    {
        var normalized = Path.GetFullPath(path);
        FileSystemWatcher? removed = null;

        lock (_rootsLock)
        {
            var match = _vaultRoots.FirstOrDefault(
                r => string.Equals(r, normalized, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return;

            // Swap the root out of the visible list FIRST: an in-flight watcher callback that
            // resolves its path after this point sees an unowned path and no-ops, even though
            // its watcher isn't disposed yet.
            var updated = new List<string>(_vaultRoots);
            updated.Remove(match);
            _vaultRoots = updated;

            if (_watchers.Remove(match, out var watcher))
                removed = watcher;
        }

        if (removed is not null)
        {
            removed.EnableRaisingEvents = false;
            removed.Dispose();
        }
    }

    /// <summary>Builds the per-root watcher: tree-refresh events carry the root, content-change
    /// detection stays path-keyed and shared across all roots.</summary>
    private FileSystemWatcher CreateWatcherForRoot(string root)
    {
        var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter         = NotifyFilters.FileName
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.LastWrite,
            EnableRaisingEvents  = true
        };

        // Tree refresh: closes over `root` so VaultChanged always tells subscribers which
        // section to refresh, without them having to re-derive it via GetOwningRoot.
        watcher.Created += (s, e) => VaultChanged?.Invoke(this, new VaultChange(root, e));
        watcher.Deleted += (s, e) => VaultChanged?.Invoke(this, new VaultChange(root, e));
        watcher.Renamed += (s, e) => VaultChanged?.Invoke(this, new VaultChange(root, new FileSystemEventArgs(
            WatcherChangeTypes.Changed, Path.GetDirectoryName(e.FullPath)!, e.Name)));

        // External content-change detection. All three routes converge on the same
        // filter so we cover every save strategy an external editor might use:
        //   • in-place overwrite (Notepad, echo)   → Changed
        //   • temp file + atomic rename (VS Code…)  → Renamed (FullPath = final name)
        //   • delete + recreate                     → Created
        // IsExternalChange suppresses our own saves and collapses duplicate bursts.
        watcher.Changed += (s, e) => NotifyIfExternalContentChange(e.FullPath, e);
        watcher.Created += (s, e) => NotifyIfExternalContentChange(e.FullPath, e);
        watcher.Renamed += (s, e) => NotifyIfExternalContentChange(e.FullPath, e);

        return watcher;
    }

    /// <summary>
    /// Routes a filesystem event through <see cref="IsExternalChange"/> and raises
    /// <see cref="FileChangedExternally"/> only when the file's on-disk content differs
    /// from the app's last write. Subscribers filter down to actually-open files.
    /// </summary>
    private void NotifyIfExternalContentChange(string fullPath, FileSystemEventArgs e)
    {
        if (IsExternalChange(fullPath))
            FileChangedExternally?.Invoke(this, e);
    }

    // ─── External-change detection ────────────────────────────────────────────

    /// <summary>
    /// Opens the self-write grace window for <paramref name="path"/>. Call BEFORE writing
    /// so a watcher event that races ahead of <see cref="RecordSelfWrite"/> is still treated
    /// as ours. See <see cref="_selfWriteGuardUntil"/>.
    /// </summary>
    public void BeginSelfWrite(string path)
    {
        lock (_writeLock) _selfWriteGuardUntil[path] = DateTime.UtcNow + SelfWriteGuard;
    }

    /// <summary>
    /// Records the on-disk write-time produced by one of the app's own writes to
    /// <paramref name="path"/>, so the watcher Changed event it triggers is later
    /// recognised as ours and suppressed by <see cref="IsExternalChange"/>. Also refreshes
    /// the grace window to cover events that arrive shortly after the write completes.
    /// </summary>
    public void RecordSelfWrite(string path)
    {
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            lock (_writeLock)
            {
                _selfWriteTimes[path]      = stamp;
                _selfWriteGuardUntil[path] = DateTime.UtcNow + SelfWriteGuard;
            }
        }
        catch { /* file vanished between write and stat — nothing to record */ }
    }

    /// <summary>
    /// Returns <c>true</c> when the current on-disk write-time of <paramref name="path"/>
    /// differs from the last write-time we know about — i.e. an external process changed
    /// the file. Our own saves are suppressed two ways: an EXACT write-time match (also
    /// collapses the duplicate events one save emits), and the self-write grace window that
    /// absorbs the watcher-vs-record race. Any genuinely new write outside that window gets a
    /// new stamp and is reported. A missing/unreadable file returns <c>false</c>.
    /// </summary>
    public bool IsExternalChange(string path)
    {
        // A missing file is never an external content change. Note: File.GetLastWriteTimeUtc
        // does NOT throw for a non-existent path — it returns a sentinel (1601-01-01 UTC) —
        // so guard existence explicitly rather than relying on the catch below.
        if (!File.Exists(path)) return false;

        DateTime actual;
        try { actual = File.GetLastWriteTimeUtc(path); }
        catch { return false; }

        lock (_writeLock)
        {
            if (_selfWriteTimes.TryGetValue(path, out var known) && actual == known)
                return false;

            if (_selfWriteGuardUntil.TryGetValue(path, out var until) && DateTime.UtcNow < until)
                return false;   // inside our own write's grace window — treat as ours

            // New stamp outside the window: remember it (suppresses the rest of this burst)
            // and report it as external.
            _selfWriteTimes[path] = actual;
            return true;
        }
    }

    // ─── Tree building ────────────────────────────────────────────────────────

    /// <summary>Builds a <see cref="VaultFile"/> tree rooted at <paramref name="path"/>.</summary>
    public VaultFile BuildTree(string path)
    {
        var root = new VaultFile
        {
            Name        = Path.GetFileName(path),
            FullPath    = path,
            IsDirectory = true
        };
        PopulateChildren(root);
        return root;
    }

    private static void PopulateChildren(VaultFile parent)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(parent.FullPath).OrderBy(d => d))
            {
                var node = new VaultFile
                {
                    Name        = Path.GetFileName(dir),
                    FullPath    = dir,
                    IsDirectory = true,
                    Parent      = parent
                };
                PopulateChildren(node);
                parent.Children.Add(node);
            }

            foreach (var file in Directory.GetFiles(parent.FullPath)
                                   .Where(f => SupportedExtensions.IsViewable(f))
                                   .OrderBy(f => f))
            {
                parent.Children.Add(new VaultFile
                {
                    Name     = Path.GetFileName(file),
                    FullPath = file,
                    Parent   = parent
                });
            }
        }
        catch (UnauthorizedAccessException) { /* skip inaccessible directories */ }
    }

    // ─── File I/O ─────────────────────────────────────────────────────────────

    /// <summary>Reads a file as UTF-8 text asynchronously.</summary>
    public Task<string> ReadFileAsync(string path) =>
        File.ReadAllTextAsync(path, System.Text.Encoding.UTF8);

    /// <summary>Writes UTF-8 text to a file asynchronously, creating it if necessary.</summary>
    public async Task WriteFileAsync(string path, string content)
    {
        // Guard BEFORE the write so a watcher event that fires (and is processed) mid-write
        // is recognised as ours; RecordSelfWrite after pins the exact stamp and extends it.
        BeginSelfWrite(path);
        await File.WriteAllTextAsync(path, content, System.Text.Encoding.UTF8);
        RecordSelfWrite(path);
    }

    /// <summary>
    /// Hermana SÍNCRONA de <see cref="WriteFileAsync"/>, con los mismos guardas de auto-escritura.
    ///
    /// Existe por UN solo llamador: el vaciado de pestañas sucias al cerrar la ventana.
    /// <c>Window.Closing</c> es sincrónico —cancelar el cierre exige decidir ANTES de volver— y
    /// bloquear ahí sobre la versión async (<c>.GetAwaiter().GetResult()</c>) se traba: el
    /// <c>await</c> de <see cref="File.WriteAllTextAsync"/> quiere volver al hilo de UI, que es
    /// justamente el que estaría bloqueado esperándolo. Escribir sincrónico evita el interbloqueo
    /// en el único momento donde perder el contenido ya no tiene vuelta atrás.
    /// </summary>
    public void WriteFile(string path, string content)
    {
        BeginSelfWrite(path);
        File.WriteAllText(path, content, System.Text.Encoding.UTF8);
        RecordSelfWrite(path);
    }

    // ─── CRUD ─────────────────────────────────────────────────────────────────

    /// <summary>Creates an empty file; appends .md if no supported extension is present.</summary>
    public string CreateFile(string directory, string name)
    {
        // A name that already ends in any viewable extension (note or code) is kept as-is;
        // anything else defaults to a markdown note.
        if (!SupportedExtensions.Viewable.Contains(Path.GetExtension(name)))
        {
            name += ".md";
        }
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, string.Empty, System.Text.Encoding.UTF8);
        return path;
    }

    /// <summary>Creates a directory, including any missing intermediaries.</summary>
    public string CreateDirectory(string parent, string name)
    {
        var path = Path.Combine(parent, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Deletes a file or directory (recursively).</summary>
    public void Delete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>Renames a file or directory; returns the new full path.</summary>
    public string Rename(string path, string newName)
    {
        var dir     = Path.GetDirectoryName(path)!;
        var newPath = Path.Combine(dir, newName);

        if (Directory.Exists(path))
            Directory.Move(path, newPath);
        else
            File.Move(path, newPath);

        return newPath;
    }

    // ─── Vault file listing ─────────────────────────────────────────────────

    /// <summary>
    /// Returns a flat, sorted list of all supported note files under <paramref name="root"/>
    /// as forward-slash relative paths (e.g. <c>subfolder/notes.md</c>), scoped to that one
    /// root only.
    /// </summary>
    public List<string> GetVaultFiles(string root)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return [];

        // Note-only: this list feeds the internal-link picker and graph, where code
        // files have no place — you don't wikilink to a .cs.
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.IsNote(f))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Legacy aggregate accessor, preserved for callers not yet migrated to
    /// <see cref="GetVaultFiles"/> (Phase 7): every note across every open root, combined.
    /// With a single root open (today's default) this is identical to the old behavior.
    /// </summary>
    public List<string> GetAllVaultFiles()
    {
        var roots = _vaultRoots;
        return roots.Count switch
        {
            0 => [],
            1 => GetVaultFiles(roots[0]),
            _ => roots.SelectMany(GetVaultFiles)
                       .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                       .ToList()
        };
    }

    // ─── Owning-root resolution ───────────────────────────────────────────────

    /// <summary>
    /// Returns the open vault root that contains <paramref name="path"/> — the longest
    /// matching prefix when roots are nested/overlapping — or <c>null</c> if the path is
    /// outside every open root (including when none are open). Never throws.
    /// </summary>
    public string? GetOwningRoot(string path)
    {
        string normalized;
        try { normalized = Path.GetFullPath(path); }
        catch { return null; }

        string? best = null;
        foreach (var root in _vaultRoots) // lock-free snapshot read
        {
            if (!IsPathUnderRoot(root, normalized)) continue;
            if (best is null || root.Length > best.Length)
                best = root;
        }
        return best;
    }

    /// <summary>
    /// True when <paramref name="path"/> is inside any currently open vault root, or when no
    /// roots are open at all (preserves the pre-multi-vault behavior of never restricting a
    /// standalone file with no vault open).
    /// </summary>
    public bool IsInsideVault(string path) => _vaultRoots.Count == 0 || GetOwningRoot(path) is not null;

    // ─── Internal link resolution ────────────────────────────────────────────

    /// <summary>
    /// Resolves an internal link target to an existing file, scoped to <paramref name="root"/>
    /// only — never searches or creates outside it. Resolution order: (1) relative to the
    /// current file's directory (for <c>[text](rel/path.md)</c> links), (2) anywhere inside
    /// <paramref name="root"/> by name/path — Obsidian-style — so a <c>[[Note]]</c> link finds
    /// <c>Note.md</c> even in another folder of the SAME vault, and (3) only if still not
    /// found, creates a new note next to the current file. Throws when the target would escape
    /// <paramref name="root"/>. A <c>null</c> root means no scope constraint (legacy behavior
    /// for a file outside every open vault).
    /// </summary>
    public string ResolveInternalLink(string? root, string target, string currentFilePath)
    {
        var currentDir = Path.GetDirectoryName(currentFilePath)!;

        var normalized = target.Replace('\\', '/').Trim();
        if (!SupportedExtensions.Note.Contains(Path.GetExtension(normalized)))
            normalized += ".md";

        // 1. Relative to the current file.
        var relResolved = Path.GetFullPath(Path.Combine(currentDir, normalized));
        if (IsPathUnderRoot(root, relResolved) && File.Exists(relResolved))
            return relResolved;

        // 2. Search the owning vault only (by relative-path suffix, then by filename).
        var found = FindInVault(root, normalized, currentDir);
        if (found is not null)
            return found;

        // 3. Not found anywhere → create a new note next to the current file.
        if (!IsPathUnderRoot(root, relResolved))
            throw new InvalidOperationException("El enlace apunta fuera del vault.");

        Directory.CreateDirectory(Path.GetDirectoryName(relResolved)!);
        File.WriteAllText(relResolved,
            $"# {Path.GetFileNameWithoutExtension(relResolved)}\n",
            System.Text.Encoding.UTF8);
        return relResolved;
    }

    /// <summary>
    /// Legacy entry point, preserved for callers not yet migrated to the root-aware overload
    /// (Phase 7): resolves <paramref name="currentFilePath"/>'s owning root itself, falling
    /// back to the top open root.
    /// </summary>
    public string ResolveInternalLink(string target, string currentFilePath)
    {
        var root = GetOwningRoot(currentFilePath) ?? VaultRoot;
        return ResolveInternalLink(root, target, currentFilePath);
    }

    /// <summary>
    /// True when <paramref name="fullPath"/> is inside <paramref name="root"/>, or when
    /// <paramref name="root"/> is <c>null</c> (no scope constraint — preserves legacy
    /// permissive behavior for a file outside every open vault).
    /// </summary>
    private static bool IsPathUnderRoot(string? root, string fullPath)
    {
        if (root is null) return true;
        var withSep = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(withSep, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds an existing file for a link target, searching only inside <paramref name="root"/>
    /// (never another open vault). A path-qualified target (<c>Chile/Incidente.md</c>) matches
    /// by relative-path suffix; a bare name (<c>Incidente.md</c>) matches by filename,
    /// preferring the current folder then the shortest path. Returns <c>null</c> when
    /// <paramref name="root"/> is <c>null</c>/missing or nothing matches.
    /// </summary>
    private static string? FindInVault(string? root, string normalizedTarget, string preferredDir)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return null;

        List<string> all;
        try
        {
            all = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(f => SupportedExtensions.IsNote(f))
                .ToList();
        }
        catch { return null; }

        // Path-qualified target → match by relative-path suffix.
        if (normalizedTarget.Contains('/'))
        {
            var suffix = "/" + normalizedTarget;
            var byPath = all.FirstOrDefault(f =>
                ("/" + Path.GetRelativePath(root, f).Replace('\\', '/'))
                    .EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (byPath is not null) return byPath;
        }

        // Bare filename → prefer the current directory, else the shortest path.
        var fileName = Path.GetFileName(normalizedTarget);
        var byName = all.Where(f =>
            string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byName.Count == 0) return null;

        return byName.FirstOrDefault(f =>
                   string.Equals(Path.GetDirectoryName(f), preferredDir, StringComparison.OrdinalIgnoreCase))
               ?? byName.OrderBy(f => f.Length).First();
    }

    // ─── Image handling ───────────────────────────────────────────────────────

    /// <summary>
    /// Copies an image to the <c>assets/</c> sub-directory of <paramref name="root"/> (or of
    /// <paramref name="fallbackDir"/> when <paramref name="root"/> is <c>null</c>), avoiding
    /// name collisions.
    /// </summary>
    public string CopyImageToAssets(string? root, string sourcePath, string? fallbackDir)
    {
        var baseDir = root ?? fallbackDir
            ?? throw new InvalidOperationException(
                "No hay ningún vault abierto y no hay directorio disponible. " +
                "Abre un vault (Archivo → Abrir vault) o guarda el archivo actual primero.");

        var assetsDir = Path.Combine(baseDir, "assets");
        Directory.CreateDirectory(assetsDir);

        var baseName  = Path.GetFileNameWithoutExtension(sourcePath);
        var ext       = Path.GetExtension(sourcePath);
        var destPath  = Path.Combine(assetsDir, baseName + ext);
        int counter   = 1;

        while (File.Exists(destPath))
            destPath = Path.Combine(assetsDir, $"{baseName}_{counter++}{ext}");

        File.Copy(sourcePath, destPath);
        return destPath;
    }

    /// <summary>
    /// Legacy entry point, preserved for callers not yet migrated to the root-aware overload
    /// (Phase 7): uses the top open root, same as the pre-multi-vault single-vault behavior.
    /// </summary>
    public string CopyImageToAssets(string sourcePath, string? fallbackDir = null) =>
        CopyImageToAssets(VaultRoot, sourcePath, fallbackDir);

    /// <summary>
    /// Guarda una imagen ya codificada (los bytes PNG del portapapeles) en la carpeta
    /// <c>attachments/</c> de <paramref name="root"/> —o de <paramref name="fallbackDir"/>
    /// cuando no hay raíz—, evitando colisiones de nombre. Devuelve la ruta escrita.
    ///
    /// No es un caso de <see cref="CopyImageToAssets"/>: el pegado no tiene archivo de origen,
    /// la imagen vive en memoria. La carpeta es <c>attachments/</c> y no <c>assets/</c> porque
    /// es donde ya está todo lo pegado en los vaults existentes (y donde el plugin Media abre
    /// su diálogo).
    /// </summary>
    public string SaveImageToAttachments(string? root, byte[] imageBytes, string? fallbackDir)
    {
        var baseDir = root ?? fallbackDir
            ?? throw new InvalidOperationException(
                "No hay ningún vault abierto y no hay directorio disponible. " +
                "Abrí un vault (Archivo → Abrir vault) o guardá el archivo actual primero.");

        var attachDir = Path.Combine(baseDir, "attachments");
        Directory.CreateDirectory(attachDir);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var destPath  = Path.Combine(attachDir, $"screenshot_{timestamp}.png");
        var counter   = 1;

        while (File.Exists(destPath))
            destPath = Path.Combine(attachDir, $"screenshot_{timestamp}_{counter++}.png");

        File.WriteAllBytes(destPath, imageBytes);
        return destPath;
    }

    /// <summary>
    /// Returns a Markdown-style image reference relative to <paramref name="root"/>.
    /// Example: <c>![image](assets/photo.png)</c>. Falls back to just the file name when
    /// <paramref name="root"/> is <c>null</c>.
    /// </summary>
    public string BuildImageMarkdown(string? root, string imagePath, string altText)
    {
        var rel = root is not null
            ? Path.GetRelativePath(root, imagePath).Replace('\\', '/')
            : Path.GetFileName(imagePath);
        return $"![{altText}]({rel})";
    }

    /// <summary>
    /// Legacy entry point, preserved for callers not yet migrated to the root-aware overload
    /// (Phase 7): uses the top open root, same as the pre-multi-vault single-vault behavior.
    /// </summary>
    public string BuildImageMarkdown(string imagePath, string altText = "image") =>
        BuildImageMarkdown(VaultRoot, imagePath, altText);

    // ─── Disposal ─────────────────────────────────────────────────────────────

    /// <summary>Stops and disposes every open root's watcher.</summary>
    public void Dispose()
    {
        lock (_rootsLock)
        {
            foreach (var watcher in _watchers.Values)
                watcher.Dispose();
            _watchers.Clear();
        }
    }
}
