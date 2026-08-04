using System.IO;
using MarkdownVault.Services;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers <see cref="FileService"/>'s external-change detection and self-write
/// suppression. The suppression is what stops the app's own saves (including
/// auto-save) from being mistaken for external edits, which would otherwise pop
/// the "file changed on disk" prompt in a loop.
/// </summary>
public class FileServiceExternalChangeTests : IDisposable
{
    private readonly string      _root;
    private readonly FileService _svc = new();

    public FileServiceExternalChangeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mvext_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void First_change_to_unknown_file_is_external()
    {
        var path = WriteFile("a.md", "hello");

        Assert.True(_svc.IsExternalChange(path));
    }

    [Fact]
    public void Self_write_is_not_external()
    {
        var path = WriteFile("b.md", "hello");
        _svc.RecordSelfWrite(path);

        Assert.False(_svc.IsExternalChange(path));
    }

    [Fact]
    public void Duplicate_change_events_collapse_to_one()
    {
        // FileSystemWatcher fires Changed multiple times per save; only the first
        // (with a new write-time) should be reported as external.
        var path = WriteFile("c.md", "hello");

        Assert.True(_svc.IsExternalChange(path));   // first burst event
        Assert.False(_svc.IsExternalChange(path));  // duplicate, same write-time
    }

    [Fact]
    public void Renamed_onto_target_is_detected_as_external()
    {
        // Simulates the temp+rename ("atomic save") strategy VS Code and others use:
        // the editor writes a temp file then renames it onto the real path. Detection
        // is event-agnostic — the Renamed handler routes the final path through the
        // same IsExternalChange filter, so it must report external just like Changed.
        var tmp    = WriteFile("e.md.tmp", "new content");
        var target = Path.Combine(_root, "e.md");
        File.Move(tmp, target);

        Assert.True(_svc.IsExternalChange(target));
    }

    [Fact]
    public void Two_distinct_external_saves_are_both_detected()
    {
        // Regression: a coarse 1s time-tolerance used to suppress a second external save
        // that landed shortly after the first, so rapid successive edits stopped updating.
        // Distinct writes get distinct write-times and must each be reported.
        var path = WriteFile("f.md", "v1");
        Assert.True(_svc.IsExternalChange(path));   // first save

        // Simulate a second external save 200 ms later (deterministic, no wall-clock wait).
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMilliseconds(200));
        Assert.True(_svc.IsExternalChange(path));   // second save must still be detected
    }

    [Fact]
    public void Missing_file_is_not_external()
    {
        var path = Path.Combine(_root, "does-not-exist.md");

        Assert.False(_svc.IsExternalChange(path));
    }

    [Fact]
    public async Task Write_through_service_is_suppressed()
    {
        var path = Path.Combine(_root, "d.md");

        await _svc.WriteFileAsync(path, "content");

        Assert.False(_svc.IsExternalChange(path));
    }

    [Fact]
    public async Task Change_within_self_write_grace_window_is_suppressed()
    {
        // Regression: the watcher Changed event runs on a thread pool and could be processed
        // before RecordSelfWrite pinned the stamp, so the app's own auto-save looked external
        // and popped the reload prompt while the user was typing. The grace window opened by
        // WriteFileAsync must suppress a change even when the observed stamp differs.
        var path = Path.Combine(_root, "g.md");
        await _svc.WriteFileAsync(path, "v1");

        // Simulate the racing event carrying a different stamp than the one we recorded.
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(5));

        Assert.False(_svc.IsExternalChange(path));  // still ours — inside the grace window
    }

    public void Dispose()
    {
        _svc.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
