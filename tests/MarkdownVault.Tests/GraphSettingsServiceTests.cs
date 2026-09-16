using System.IO;
using MarkdownVault.Models;
using MarkdownVault.Services;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers <see cref="GraphSettingsService"/>: one settings file per vault, written outside the
/// vault so nothing appears among the notes and the vault watcher never fires on a slider drag.
/// </summary>
public class GraphSettingsServiceTests : IDisposable
{
    private readonly string _directory;

    public GraphSettingsServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"mvgraphcfg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void A_vault_that_was_never_configured_opens_with_the_calibrated_defaults()
    {
        using var service = new GraphSettingsService(_directory);

        var settings = service.Load(@"C:\vaults\nunca-tocado");

        Assert.Equal(1.0, settings.NodeScale);
        Assert.Equal(0, settings.MinDegree);
        Assert.True(settings.ShowLabels);
        Assert.True(settings.ClusterByFolder);
        Assert.False(settings.ThreeD);
    }

    [Fact]
    public void What_was_saved_comes_back()
    {
        using var service = new GraphSettingsService(_directory);
        const string vault = @"C:\vaults\wiki";

        service.Save(vault, new GraphSettings
        {
            NodeScale     = 0.6,
            MinDegree     = 2,
            ThreeD        = true,
            ForceRepel    = 2.5,
            HiddenFolders = ["fuentes", "sintesis"]
        });
        service.Flush();

        var back = service.Load(vault);

        Assert.Equal(0.6, back.NodeScale);
        Assert.Equal(2, back.MinDegree);
        Assert.True(back.ThreeD);
        Assert.Equal(2.5, back.ForceRepel);
        Assert.Equal(new[] { "fuentes", "sintesis" }, back.HiddenFolders);
    }

    [Fact]
    public void Each_vault_keeps_its_own_settings()
    {
        using var service = new GraphSettingsService(_directory);

        service.Save(@"C:\vaults\denso", new GraphSettings { NodeScale = 0.5 });
        service.Flush();
        service.Save(@"C:\vaults\disperso", new GraphSettings { NodeScale = 2.0 });
        service.Flush();

        Assert.Equal(0.5, service.Load(@"C:\vaults\denso").NodeScale);
        Assert.Equal(2.0, service.Load(@"C:\vaults\disperso").NodeScale);
    }

    [Fact]
    public void Switching_vault_before_the_write_lands_does_not_file_it_under_the_wrong_name()
    {
        // The debounce holds one pending write. Moving to another vault inside that window has to
        // flush the first one FIRST, or the second vault's settings get stamped on the first.
        using var service = new GraphSettingsService(_directory);

        service.Save(@"C:\vaults\primero", new GraphSettings { NodeScale = 0.4 });
        service.Save(@"C:\vaults\segundo", new GraphSettings { NodeScale = 1.9 });
        service.Flush();

        Assert.Equal(0.4, service.Load(@"C:\vaults\primero").NodeScale);
        Assert.Equal(1.9, service.Load(@"C:\vaults\segundo").NodeScale);
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_defaults_instead_of_throwing()
    {
        const string vault = @"C:\vaults\roto";
        File.WriteAllText(Path.Combine(_directory, GraphSettingsService.FileNameFor(vault)),
                          "{ esto no es json valido");

        using var service = new GraphSettingsService(_directory);

        Assert.Equal(1.0, service.Load(vault).NodeScale);
    }

    [Fact]
    public void The_same_vault_maps_to_the_same_file_however_the_path_is_written()
    {
        // Trailing slash, separator style and casing all describe the same folder. If they mapped
        // to different files, the settings would vanish depending on how the vault was opened.
        var a = GraphSettingsService.FileNameFor(@"C:\vaults\Wiki");
        var b = GraphSettingsService.FileNameFor(@"c:/vaults/wiki/");
        var c = GraphSettingsService.FileNameFor(@"C:\VAULTS\WIKI");

        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void Two_vaults_with_the_same_folder_name_do_not_collide()
    {
        var a = GraphSettingsService.FileNameFor(@"C:\proyectos\alfa\wiki");
        var b = GraphSettingsService.FileNameFor(@"C:\proyectos\beta\wiki");

        Assert.NotEqual(a, b);
        Assert.StartsWith("wiki-", a);   // readable prefix so the folder can be read by hand
        Assert.StartsWith("wiki-", b);
    }

    [Fact]
    public void The_file_name_is_pinned_so_upgrades_do_not_orphan_anyone()
    {
        // Two things at once. First, the name must be the same on EVERY run: it is derived with
        // FNV-1a rather than string.GetHashCode precisely because .NET randomises string hashing
        // per process, which would hand each launch a different file and lose every setting.
        //
        // Second, this literal locks the algorithm down. Change how the name is built and this
        // test goes red — which is the point, because in the field that change would silently
        // orphan every settings file already on disk. If it is ever changed deliberately, it needs
        // a migration, not a new expected value.
        Assert.Equal("wiki-8b894315.json", GraphSettingsService.FileNameFor(@"C:\vaults\wiki"));
    }

    [Fact]
    public void A_path_with_nothing_usable_in_its_name_still_produces_a_valid_file()
    {
        var name = GraphSettingsService.FileNameFor(@"C:\...\!!!");

        Assert.DoesNotContain(Path.GetInvalidFileNameChars(), name.Contains);
        Assert.EndsWith(".json", name);
    }

    [Fact]
    public void An_empty_vault_path_is_ignored_rather_than_writing_a_stray_file()
    {
        using var service = new GraphSettingsService(_directory);

        service.Save("", new GraphSettings { NodeScale = 0.3 });
        service.Flush();

        Assert.Empty(Directory.GetFiles(_directory));
        Assert.Equal(1.0, service.Load("").NodeScale);
    }

    [Fact]
    public void Disposing_writes_whatever_was_still_pending()
    {
        const string vault = @"C:\vaults\cerrado-rapido";

        using (var service = new GraphSettingsService(_directory))
            service.Save(vault, new GraphSettings { NodeScale = 0.75 });

        using var reopened = new GraphSettingsService(_directory);
        Assert.Equal(0.75, reopened.Load(vault).NodeScale);
    }
}
