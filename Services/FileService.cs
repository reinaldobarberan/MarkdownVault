using System.IO;
using MarkdownVault.Models;

namespace MarkdownVault.Services;

/// <summary>
/// Manages vault I/O: opening vaults, building the file tree, CRUD operations,
/// and watching for external changes via <see cref="FileSystemWatcher"/>.
/// </summary>
public class FileService : IDisposable
{
    private FileSystemWatcher? _watcher;

    /// <summary>Root directory of the currently open vault, or <c>null</c> when none is open.</summary>
    public string? VaultRoot { get; private set; }

    /// <summary>Raised on the thread pool whenever the vault's file system changes.</summary>
    public event EventHandler<FileSystemEventArgs>? VaultChanged;

    // ─── Vault lifecycle ──────────────────────────────────────────────────────

    /// <summary>Sets the vault root and starts watching for external changes.</summary>
    public void OpenVault(string path)
    {
        VaultRoot = path;

        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter         = NotifyFilters.FileName
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.LastWrite,
            EnableRaisingEvents  = true
        };
        _watcher.Created += (s, e) => VaultChanged?.Invoke(this, e);
        _watcher.Deleted += (s, e) => VaultChanged?.Invoke(this, e);
        _watcher.Renamed += (s, e) => VaultChanged?.Invoke(this, new FileSystemEventArgs(
            WatcherChangeTypes.Changed, Path.GetDirectoryName(e.FullPath)!, e.Name));
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

            var supportedExtensions = new[] { ".md", ".mermaid", ".mmd", ".html", ".htm" };
            foreach (var file in Directory.GetFiles(parent.FullPath)
                                   .Where(f => supportedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
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
    public Task WriteFileAsync(string path, string content) =>
        File.WriteAllTextAsync(path, content, System.Text.Encoding.UTF8);

    // ─── CRUD ─────────────────────────────────────────────────────────────────

    /// <summary>Creates an empty file; appends .md if no supported extension is present.</summary>
    public string CreateFile(string directory, string name)
    {
        var supportedExtensions = new[] { ".md", ".mermaid", ".mmd", ".html", ".htm" };
        bool hasSupportedExt = false;
        foreach (var ext in supportedExtensions)
        {
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                hasSupportedExt = true;
                break;
            }
        }
        if (!hasSupportedExt)
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
    /// Returns a flat, sorted list of all supported files in the vault as
    /// forward-slash relative paths (e.g. <c>subfolder/notes.md</c>).
    /// </summary>
    public List<string> GetAllVaultFiles()
    {
        if (string.IsNullOrEmpty(VaultRoot) || !Directory.Exists(VaultRoot))
            return [];

        var supportedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".md", ".mermaid", ".mmd", ".html", ".htm" };

        return Directory.EnumerateFiles(VaultRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedExts.Contains(Path.GetExtension(f)))
            .Select(f => Path.GetRelativePath(VaultRoot, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ─── Internal link resolution ────────────────────────────────────────────

    private static readonly string[] SupportedLinkExts =
        { ".md", ".mermaid", ".mmd", ".html", ".htm" };

    /// <summary>
    /// Resolves an internal link target to an existing file. Resolution order:
    /// (1) relative to the current file's directory (for <c>[text](rel/path.md)</c>
    /// links), (2) anywhere in the vault by name/path — Obsidian-style — so a
    /// <c>[[Note]]</c> link finds <c>Note.md</c> even in another folder, and
    /// (3) only if still not found, creates a new note next to the current file.
    /// Throws when the target would escape the vault root.
    /// </summary>
    public string ResolveInternalLink(string target, string currentFilePath)
    {
        var currentDir = Path.GetDirectoryName(currentFilePath)!;

        var normalized = target.Replace('\\', '/').Trim();
        if (!SupportedLinkExts.Contains(
                Path.GetExtension(normalized), StringComparer.OrdinalIgnoreCase))
            normalized += ".md";

        // 1. Relative to the current file.
        var relResolved = Path.GetFullPath(Path.Combine(currentDir, normalized));
        if (IsInsideVault(relResolved) && File.Exists(relResolved))
            return relResolved;

        // 2. Search the whole vault (by relative-path suffix, then by filename).
        var found = FindInVault(normalized, currentDir);
        if (found is not null)
            return found;

        // 3. Not found anywhere → create a new note next to the current file.
        if (!IsInsideVault(relResolved))
            throw new InvalidOperationException("El enlace apunta fuera del vault.");

        Directory.CreateDirectory(Path.GetDirectoryName(relResolved)!);
        File.WriteAllText(relResolved,
            $"# {Path.GetFileNameWithoutExtension(relResolved)}\n",
            System.Text.Encoding.UTF8);
        return relResolved;
    }

    /// <summary>True when <paramref name="fullPath"/> is inside the open vault (or no vault is open).</summary>
    private bool IsInsideVault(string fullPath)
    {
        if (VaultRoot is null) return true;
        var root = VaultRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullPath, VaultRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds an existing vault file for a link target. A path-qualified target
    /// (<c>Chile/Incidente.md</c>) matches by relative-path suffix; a bare name
    /// (<c>Incidente.md</c>) matches by filename, preferring the current folder
    /// then the shortest path.
    /// </summary>
    private string? FindInVault(string normalizedTarget, string preferredDir)
    {
        if (string.IsNullOrEmpty(VaultRoot) || !Directory.Exists(VaultRoot))
            return null;

        List<string> all;
        try
        {
            all = Directory.EnumerateFiles(VaultRoot, "*.*", SearchOption.AllDirectories)
                .Where(f => SupportedLinkExts.Contains(
                    Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        catch { return null; }

        // Path-qualified target → match by relative-path suffix.
        if (normalizedTarget.Contains('/'))
        {
            var suffix = "/" + normalizedTarget;
            var byPath = all.FirstOrDefault(f =>
                ("/" + Path.GetRelativePath(VaultRoot, f).Replace('\\', '/'))
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
    /// Copies an image to the <c>assets/</c> sub-directory of the vault (or of
    /// <paramref name="fallbackDir"/> when no vault is open), avoiding name collisions.
    /// </summary>
    public string CopyImageToAssets(string sourcePath, string? fallbackDir = null)
    {
        var baseDir = VaultRoot ?? fallbackDir
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
    /// Returns a Markdown-style image reference relative to the vault root.
    /// Example: <c>![image](assets/photo.png)</c>
    /// </summary>
    public string BuildImageMarkdown(string imagePath, string altText = "image")
    {
        var rel = VaultRoot is not null
            ? Path.GetRelativePath(VaultRoot, imagePath).Replace('\\', '/')
            : Path.GetFileName(imagePath);
        return $"![{altText}]({rel})";
    }

    public void Dispose() => _watcher?.Dispose();
}
