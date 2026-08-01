using System;
using System.IO;
using System.Threading.Tasks;
using Markdig;
using MarkdownVault.Plugin.Eisenhower;
using MarkdownVault.PluginSdk;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Prueba la integración de la extensión Markdig (Batch 4): que el renderer se registre
/// y despache correctamente para bloques ```eisenhower``` vs cualquier otro bloque de
/// código, usando un pipeline Markdig real (no un pipeline simulado) para no dar por
/// sentado el mecanismo exacto de dispatch de Markdig 1.1.2.
/// </summary>
public class EisenhowerRenderExtensionTests
{
    /// <summary>IPluginStorage mínimo respaldado por una carpeta temporal real en disco.</summary>
    private sealed class FakeStorage : IPluginStorage, IDisposable
    {
        public string RootPath { get; }

        public FakeStorage()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "EisenhowerRenderTests_" + Guid.NewGuid());
            Directory.CreateDirectory(RootPath);
        }

        public Task<string> ReadTextAsync(string relativePath) =>
            Task.FromResult(File.ReadAllText(Path.Combine(RootPath, relativePath)));

        public Task WriteTextAsync(string relativePath, string content)
        {
            File.WriteAllText(Path.Combine(RootPath, relativePath), content);
            return Task.CompletedTask;
        }

        public bool Exists(string relativePath) => File.Exists(Path.Combine(RootPath, relativePath));

        public void Delete(string relativePath)
        {
            var path = Path.Combine(RootPath, relativePath);
            if (File.Exists(path)) File.Delete(path);
        }

        public void Dispose()
        {
            try { Directory.Delete(RootPath, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    private static MarkdownPipeline BuildPipeline(IPluginStorage storage)
    {
        var builder = new MarkdownPipelineBuilder();
        var contribution = new EisenhowerContribution(storage);
        Assert.IsAssignableFrom<IMarkdownExtension>(contribution.CreateMarkdigExtension());
        builder.Extensions.Add((IMarkdownExtension)contribution.CreateMarkdigExtension());
        return builder.Build();
    }

    [Fact]
    public void Eisenhower_fenced_block_renders_grid_with_tasks_from_storage()
    {
        using var storage = new FakeStorage();
        var task = new TaskItem(Guid.NewGuid(), "Tarea desde disco", true, true, false,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        File.WriteAllText(Path.Combine(storage.RootPath, "tasks.json"), TaskStore.Serialize(new[] { task }));

        var pipeline = BuildPipeline(storage);
        var html = Markdig.Markdown.ToHtml("```eisenhower\n```\n", pipeline);

        Assert.Contains("eisenhower-grid", html);
        Assert.Contains("Tarea desde disco", html);
        Assert.DoesNotContain("eisenhower-error", html);
    }

    [Fact]
    public void Eisenhower_fenced_block_with_no_tasks_json_renders_empty_grid_not_error()
    {
        using var storage = new FakeStorage(); // sandbox exists, tasks.json does not

        var pipeline = BuildPipeline(storage);
        var html = Markdig.Markdown.ToHtml("```eisenhower\n```\n", pipeline);

        Assert.Contains("eisenhower-grid", html);
        Assert.DoesNotContain("eisenhower-error", html);
    }

    [Fact]
    public void Eisenhower_fenced_block_with_corrupt_tasks_json_renders_error_banner()
    {
        using var storage = new FakeStorage();
        File.WriteAllText(Path.Combine(storage.RootPath, "tasks.json"), "{ not valid json");

        var pipeline = BuildPipeline(storage);
        var html = Markdig.Markdown.ToHtml("```eisenhower\n```\n", pipeline);

        Assert.Contains("eisenhower-error", html);
        Assert.DoesNotContain("eisenhower-grid", html);
    }

    [Fact]
    public void Other_fenced_code_blocks_are_unaffected_by_the_extension()
    {
        using var storage = new FakeStorage();

        var pipeline = BuildPipeline(storage);
        var html = Markdig.Markdown.ToHtml("```csharp\nvar x = 1;\n```\n", pipeline);

        Assert.Contains("language-csharp", html);
        Assert.Contains("var x = 1;", html);
        Assert.DoesNotContain("eisenhower-grid", html);
        Assert.DoesNotContain("eisenhower-error", html);
    }

    [Fact]
    public void Plain_paragraphs_and_other_markdown_are_unaffected_by_the_extension()
    {
        using var storage = new FakeStorage();

        var pipeline = BuildPipeline(storage);
        var html = Markdig.Markdown.ToHtml("# Titulo\n\nUn parrafo normal.\n", pipeline);

        Assert.Contains("<h1>Titulo</h1>", html);
        Assert.Contains("Un parrafo normal.", html);
    }
}
