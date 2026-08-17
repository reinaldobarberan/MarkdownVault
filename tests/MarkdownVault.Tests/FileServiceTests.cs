using System.IO;
using MarkdownVault.Services;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers <see cref="FileService"/>'s multi-vault surface (Phase 1, multi-vault change):
/// owning-root resolution, root add/remove idempotency + watcher disposal, per-root file
/// listing, and that wikilink/asset resolution never crosses into another open vault root
/// (vault-scoped-resolution spec). Real temp directories, no mocking framework — matches
/// the project's existing convention (see <see cref="FileServiceExternalChangeTests"/>).
/// </summary>
public class FileServiceTests : IDisposable
{
    private readonly string      _baseDir;
    private readonly string      _rootA;
    private readonly string      _rootB;
    private readonly FileService _svc = new();

    public FileServiceTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), $"mvmulti_{Guid.NewGuid():N}");
        _rootA   = Path.Combine(_baseDir, "VaultA");
        _rootB   = Path.Combine(_baseDir, "VaultB");
        Directory.CreateDirectory(_rootA);
        Directory.CreateDirectory(_rootB);
    }

    // ─── GetOwningRoot ───────────────────────────────────────────────────────

    [Fact]
    public void GetOwningRoot_returns_the_root_containing_the_path()
    {
        _svc.AddRoot(_rootA);
        _svc.AddRoot(_rootB);
        var file = Path.Combine(_rootA, "note.md");

        Assert.Equal(Path.GetFullPath(_rootA), _svc.GetOwningRoot(file));
    }

    [Fact]
    public void GetOwningRoot_returns_the_innermost_root_when_nested_or_overlapping()
    {
        var inner = Path.Combine(_rootA, "Nested");
        Directory.CreateDirectory(inner);
        _svc.AddRoot(_rootA);
        _svc.AddRoot(inner);

        var file = Path.Combine(inner, "note.md");

        // The innermost (longest-prefix) root wins, per design's owning-root resolution.
        Assert.Equal(Path.GetFullPath(inner), _svc.GetOwningRoot(file));
    }

    [Fact]
    public void GetOwningRoot_returns_null_for_a_path_outside_every_open_root()
    {
        _svc.AddRoot(_rootA);
        var outsidePath = Path.Combine(
            Path.GetTempPath(), $"elsewhere_{Guid.NewGuid():N}", "note.md");

        Assert.Null(_svc.GetOwningRoot(outsidePath));
    }

    [Fact]
    public void GetOwningRoot_returns_null_when_no_roots_are_open()
    {
        Assert.Null(_svc.GetOwningRoot(Path.Combine(_rootA, "note.md")));
    }

    // ─── AddRoot / RemoveRoot ────────────────────────────────────────────────

    [Fact]
    public void AddRoot_is_idempotent_and_case_insensitive()
    {
        _svc.AddRoot(_rootA);
        _svc.AddRoot(_rootA.ToUpperInvariant());
        _svc.AddRoot(_rootA);

        Assert.Single(_svc.VaultRoots);
    }

    [Fact]
    public void AddRoot_opens_several_distinct_roots_at_once()
    {
        _svc.AddRoot(_rootA);
        _svc.AddRoot(_rootB);

        Assert.Equal(2, _svc.VaultRoots.Count);
    }

    [Fact]
    public void RemoveRoot_drops_the_root_and_is_idempotent()
    {
        _svc.AddRoot(_rootA);
        _svc.AddRoot(_rootB);

        _svc.RemoveRoot(_rootA);

        Assert.DoesNotContain(_svc.VaultRoots,
            r => string.Equals(r, Path.GetFullPath(_rootA), StringComparison.OrdinalIgnoreCase));
        Assert.Single(_svc.VaultRoots);

        // Removing an already-closed root must be a silent no-op, not a throw.
        var ex = Record.Exception(() => _svc.RemoveRoot(_rootA));
        Assert.Null(ex);
    }

    [Fact]
    public async Task RemoveRoot_disposes_the_watcher_so_no_further_VaultChanged_events_fire()
    {
        var events = new List<VaultChange>();
        _svc.VaultChanged += (_, e) => { lock (events) events.Add(e); };
        _svc.AddRoot(_rootA);

        File.WriteAllText(Path.Combine(_rootA, "a.md"), "x");
        Assert.True(
            await WaitUntilAsync(() => events.Count > 0, TimeSpan.FromSeconds(3)),
            "expected VaultChanged to fire while the watcher was still active");

        _svc.RemoveRoot(_rootA);
        lock (events) events.Clear();

        File.WriteAllText(Path.Combine(_rootA, "b.md"), "y");
        // Give a wrongly-still-active watcher every chance to misfire before asserting silence.
        await Task.Delay(500);
        lock (events) Assert.Empty(events);
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

    // ─── GetVaultFiles scoping ───────────────────────────────────────────────

    [Fact]
    public void GetVaultFiles_is_scoped_to_the_given_root_only()
    {
        File.WriteAllText(Path.Combine(_rootA, "A.md"), "# A");
        File.WriteAllText(Path.Combine(_rootB, "B.md"), "# B");
        _svc.AddRoot(_rootA);
        _svc.AddRoot(_rootB);

        var filesA = _svc.GetVaultFiles(Path.GetFullPath(_rootA));

        Assert.Contains("A.md", filesA);
        Assert.DoesNotContain("B.md", filesA);
    }

    // ─── Vault-scoped internal-link / asset resolution ──────────────────────

    [Fact]
    public void ResolveInternalLink_owning_root_never_crosses_into_another_open_root()
    {
        // A same-named target exists in BOTH vaults. Resolution scoped to root A must
        // return vault A's copy only, never vault B's, even with both open (spec:
        // "Wikilink resolves within same vault"). Source sits in a subfolder so the
        // relative-to-current-dir step (1) can't accidentally match — only the
        // root-scoped vault search (step 2) can find it.
        var notesDir = Path.Combine(_rootA, "Notes");
        Directory.CreateDirectory(notesDir);
        var sourceInA = Path.Combine(notesDir, "Source.md");
        File.WriteAllText(sourceInA, "[[Target]]");

        var targetA = Path.Combine(_rootA, "Target.md");
        var targetB = Path.Combine(_rootB, "Target.md");
        File.WriteAllText(targetA, "# A's Target");
        File.WriteAllText(targetB, "# B's Target");

        _svc.AddRoot(_rootA);
        _svc.AddRoot(_rootB);

        var resolved = _svc.ResolveInternalLink(Path.GetFullPath(_rootA), "Target", sourceInA);

        Assert.Equal(Path.GetFullPath(targetA), Path.GetFullPath(resolved));
    }

    [Fact]
    public void CopyImageToAssets_writes_under_the_given_roots_assets_folder_not_the_fallback()
    {
        var source = Path.Combine(_rootA, "src.png");
        File.WriteAllBytes(source, [1, 2, 3]);

        // root = vault B, fallbackDir = vault A: with a non-null root, fallback must be
        // ignored entirely — the image lands in B's assets/, never A's.
        var dest = _svc.CopyImageToAssets(Path.GetFullPath(_rootB), source, fallbackDir: _rootA);

        Assert.StartsWith(Path.Combine(Path.GetFullPath(_rootB), "assets"), dest);
        Assert.DoesNotContain(Path.GetFullPath(_rootA), dest, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _svc.Dispose();
        try { Directory.Delete(_baseDir, recursive: true); } catch { /* best effort */ }
    }
}
