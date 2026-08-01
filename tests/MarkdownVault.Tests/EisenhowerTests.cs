using System;
using System.Collections.Generic;
using System.Linq;
using MarkdownVault.Plugin.Eisenhower;
using Xunit;

namespace MarkdownVault.Tests;

public class EisenhowerTests
{
    // -- Quadrant truth table -------------------------------------------------

    [Theory]
    [InlineData(true,  true,  "Hacer ahora")]
    [InlineData(false, true,  "Planificar")]
    [InlineData(true,  false, "Delegar")]
    [InlineData(false, false, "Eliminar")]
    public void GetQuadrant_maps_urgent_important_to_label(bool urgent, bool important, string expected)
    {
        Assert.Equal(expected, TaskStore.GetQuadrant(urgent, important));
    }

    [Theory]
    [InlineData(true,  true,  "Hacer ahora")]
    [InlineData(false, true,  "Planificar")]
    [InlineData(true,  false, "Delegar")]
    [InlineData(false, false, "Eliminar")]
    public void TaskItem_Quadrant_is_computed_from_urgent_and_important(bool urgent, bool important, string expected)
    {
        var task = new TaskItem(Guid.NewGuid(), "Title", urgent, important, false,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        Assert.Equal(expected, task.Quadrant);
    }

    // -- JSON round-trip --------------------------------------------------------

    [Fact]
    public void Serialize_then_Load_round_trips_task_list()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = new List<TaskItem>
        {
            new(Guid.NewGuid(), "Pagar impuestos",   true,  true,  false, now, now),
            new(Guid.NewGuid(), "Leer un libro",      false, false, false, now, now),
            new(Guid.NewGuid(), "Responder correos",  true,  false, true,  now, now),
        };

        var json = TaskStore.Serialize(tasks);
        var result = TaskStore.Load(json);

        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.Equal(tasks.Count, result.Tasks.Count);
        for (var i = 0; i < tasks.Count; i++)
        {
            Assert.Equal(tasks[i], result.Tasks[i]);
        }
    }

    [Fact]
    public void Serialize_writes_version_field()
    {
        var json = TaskStore.Serialize(Array.Empty<TaskItem>());

        Assert.Contains("\"version\"", json);
        Assert.Contains(TaskStore.SchemaVersion.ToString(), json);
    }

    // -- Load discriminated result -----------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_null_or_whitespace_returns_Empty(string? raw)
    {
        var result = TaskStore.Load(raw);

        Assert.Equal(LoadStatus.Empty, result.Status);
        Assert.Empty(result.Tasks);
    }

    [Theory]
    [InlineData("{ not valid json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"just a string\"")]
    public void Load_malformed_json_returns_Corrupt(string raw)
    {
        var result = TaskStore.Load(raw);

        Assert.Equal(LoadStatus.Corrupt, result.Status);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public void Load_newer_schema_version_returns_NewerVersion()
    {
        var raw = $"{{ \"version\": {TaskStore.SchemaVersion + 1}, \"tasks\": [] }}";

        var result = TaskStore.Load(raw);

        Assert.Equal(LoadStatus.NewerVersion, result.Status);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public void Load_current_schema_version_returns_Ok()
    {
        var raw = $"{{ \"version\": {TaskStore.SchemaVersion}, \"tasks\": [] }}";

        var result = TaskStore.Load(raw);

        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.Empty(result.Tasks);
    }

    // -- Capture (pure) -----------------------------------------------------

    [Fact]
    public void Capture_builds_TaskItem_from_title_urgent_important()
    {
        var current = new LoadResult(LoadStatus.Empty, Array.Empty<TaskItem>());

        var result = TaskStore.Capture(current, "Comprar leche", urgent: true, important: false);

        Assert.Equal(CaptureStatus.Ok, result.Status);
        Assert.NotNull(result.Task);
        Assert.NotEqual(Guid.Empty, result.Task!.Id);
        Assert.Equal("Comprar leche", result.Task.Title);
        Assert.True(result.Task.Urgent);
        Assert.False(result.Task.Important);
        Assert.False(result.Task.Done);
        Assert.Equal(result.Task.CreatedAt, result.Task.UpdatedAt);
        Assert.NotNull(result.Json);
    }

    [Fact]
    public void Capture_appends_to_existing_list_and_returns_serialized_json()
    {
        var now = DateTimeOffset.UtcNow;
        var existing = new List<TaskItem>
        {
            new(Guid.NewGuid(), "Tarea existente 1", true, true, false, now, now),
            new(Guid.NewGuid(), "Tarea existente 2", false, true, false, now, now),
        };
        var current = new LoadResult(LoadStatus.Ok, existing);

        var result = TaskStore.Capture(current, "Tarea nueva", urgent: false, important: false);

        Assert.Equal(CaptureStatus.Ok, result.Status);
        Assert.NotNull(result.Json);

        var reloaded = TaskStore.Load(result.Json);
        Assert.Equal(LoadStatus.Ok, reloaded.Status);
        Assert.Equal(3, reloaded.Tasks.Count);
        Assert.Equal(existing[0], reloaded.Tasks[0]);
        Assert.Equal(existing[1], reloaded.Tasks[1]);
        Assert.Equal("Tarea nueva", reloaded.Tasks[2].Title);
        Assert.Equal(result.Task, reloaded.Tasks[2]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Capture_rejects_blank_or_whitespace_title(string? title)
    {
        var current = new LoadResult(LoadStatus.Empty, Array.Empty<TaskItem>());

        var result = TaskStore.Capture(current, title, urgent: true, important: true);

        Assert.Equal(CaptureStatus.BlankTitle, result.Status);
        Assert.Null(result.Task);
        Assert.Null(result.Json);
    }

    [Fact]
    public void Capture_aborts_when_current_load_is_Corrupt()
    {
        var current = new LoadResult(LoadStatus.Corrupt, Array.Empty<TaskItem>());

        var result = TaskStore.Capture(current, "Tarea nueva", urgent: true, important: true);

        Assert.Equal(CaptureStatus.Unreadable, result.Status);
        Assert.Null(result.Task);
        Assert.Null(result.Json);
    }

    [Fact]
    public void Capture_aborts_when_current_load_is_NewerVersion()
    {
        var current = new LoadResult(LoadStatus.NewerVersion, Array.Empty<TaskItem>());

        var result = TaskStore.Capture(current, "Tarea nueva", urgent: false, important: true);

        Assert.Equal(CaptureStatus.Unreadable, result.Status);
        Assert.Null(result.Task);
        Assert.Null(result.Json);
    }

    // -- RenderGridHtml (pure) -----------------------------------------------

    /// <summary>Extrae el fragmento HTML de un cuadrante (desde su marcador data-quadrant hasta el siguiente, o el final).</summary>
    private static string ExtractQuadrantSection(string html, string quadrantLabel)
    {
        var marker = $"data-quadrant=\"{quadrantLabel}\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Cuadrante '{quadrantLabel}' no encontrado en el HTML: {html}");

        var nextStart = html.IndexOf("data-quadrant=\"", start + marker.Length, StringComparison.Ordinal);
        return nextStart >= 0 ? html[start..nextStart] : html[start..];
    }

    [Fact]
    public void RenderGridHtml_Ok_emits_four_quadrant_containers()
    {
        var result = new LoadResult(LoadStatus.Ok, Array.Empty<TaskItem>());

        var html = TaskStore.RenderGridHtml(result);

        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(html, "data-quadrant=\"").Count);
        Assert.Contains("data-quadrant=\"Hacer ahora\"", html);
        Assert.Contains("data-quadrant=\"Planificar\"", html);
        Assert.Contains("data-quadrant=\"Delegar\"", html);
        Assert.Contains("data-quadrant=\"Eliminar\"", html);
        Assert.DoesNotContain("eisenhower-error", html);
    }

    [Fact]
    public void RenderGridHtml_Ok_places_each_task_in_its_computed_quadrant()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = new List<TaskItem>
        {
            new(Guid.NewGuid(), "Tarea Hacer ahora", true,  true,  false, now, now),
            new(Guid.NewGuid(), "Tarea Planificar",  false, true,  false, now, now),
            new(Guid.NewGuid(), "Tarea Delegar",     true,  false, false, now, now),
            new(Guid.NewGuid(), "Tarea Eliminar",    false, false, false, now, now),
        };
        var result = new LoadResult(LoadStatus.Ok, tasks);

        var html = TaskStore.RenderGridHtml(result);

        var doSection       = ExtractQuadrantSection(html, "Hacer ahora");
        var planSection     = ExtractQuadrantSection(html, "Planificar");
        var delegateSection = ExtractQuadrantSection(html, "Delegar");
        var eliminateSection = ExtractQuadrantSection(html, "Eliminar");

        Assert.Contains("Tarea Hacer ahora", doSection);
        Assert.Contains("Tarea Planificar", planSection);
        Assert.Contains("Tarea Delegar", delegateSection);
        Assert.Contains("Tarea Eliminar", eliminateSection);

        // Cada tarea debe aparecer SOLO en su cuadrante, no en los otros tres.
        Assert.DoesNotContain("Tarea Hacer ahora", planSection);
        Assert.DoesNotContain("Tarea Hacer ahora", delegateSection);
        Assert.DoesNotContain("Tarea Hacer ahora", eliminateSection);
        Assert.DoesNotContain("Tarea Planificar", doSection);
        Assert.DoesNotContain("Tarea Delegar", doSection);
        Assert.DoesNotContain("Tarea Eliminar", doSection);
    }

    [Fact]
    public void RenderGridHtml_Empty_status_renders_four_empty_quadrants_without_error()
    {
        var result = new LoadResult(LoadStatus.Empty, Array.Empty<TaskItem>());

        var html = TaskStore.RenderGridHtml(result);

        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(html, "data-quadrant=\"").Count);
        Assert.DoesNotContain("<li", html);
        Assert.DoesNotContain("eisenhower-error", html);
    }

    [Fact]
    public void RenderGridHtml_Corrupt_status_renders_distinct_error_banner_not_quadrants()
    {
        var result = new LoadResult(LoadStatus.Corrupt, Array.Empty<TaskItem>());

        var html = TaskStore.RenderGridHtml(result);

        Assert.Contains("eisenhower-error", html);
        Assert.DoesNotContain("data-quadrant=\"", html);
        Assert.DoesNotContain("eisenhower-grid", html);
    }

    [Fact]
    public void RenderGridHtml_NewerVersion_status_renders_distinct_error_banner_not_quadrants()
    {
        var result = new LoadResult(LoadStatus.NewerVersion, Array.Empty<TaskItem>());

        var html = TaskStore.RenderGridHtml(result);

        Assert.Contains("eisenhower-error", html);
        Assert.DoesNotContain("data-quadrant=\"", html);
        Assert.DoesNotContain("eisenhower-grid", html);
    }

    [Fact]
    public void RenderGridHtml_Corrupt_and_NewerVersion_produce_different_messages()
    {
        var corruptHtml = TaskStore.RenderGridHtml(new LoadResult(LoadStatus.Corrupt, Array.Empty<TaskItem>()));
        var newerHtml   = TaskStore.RenderGridHtml(new LoadResult(LoadStatus.NewerVersion, Array.Empty<TaskItem>()));

        Assert.NotEqual(corruptHtml, newerHtml);
    }

    [Fact]
    public void RenderGridHtml_never_throws_for_any_status()
    {
        foreach (LoadStatus status in Enum.GetValues(typeof(LoadStatus)))
        {
            var result = new LoadResult(status, Array.Empty<TaskItem>());
            var ex = Record.Exception(() => TaskStore.RenderGridHtml(result));
            Assert.Null(ex);
        }
    }

    [Fact]
    public void RenderGridHtml_escapes_html_in_task_titles_to_prevent_xss()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = new List<TaskItem>
        {
            new(Guid.NewGuid(), "<script>alert(1)</script>", true, true, false, now, now),
        };
        var result = new LoadResult(LoadStatus.Ok, tasks);

        var html = TaskStore.RenderGridHtml(result);

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    // -- ToggleDone (pure, in-memory list for the future Eisenhower window) --------

    [Fact]
    public void ToggleDone_flips_false_to_true_and_bumps_updatedAt()
    {
        var created = DateTimeOffset.UtcNow.AddMinutes(-10);
        var task = new TaskItem(Guid.NewGuid(), "Tarea", true, true, false, created, created);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.ToggleDone(tasks, task.Id);

        Assert.Single(result.Tasks);
        Assert.True(result.Tasks[0].Done);
        Assert.True(result.Tasks[0].UpdatedAt > created);
        Assert.NotNull(result.Json);
    }

    [Fact]
    public void ToggleDone_flips_true_back_to_false()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Tarea", true, true, true, now, now);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.ToggleDone(tasks, task.Id);

        Assert.False(result.Tasks[0].Done);
    }

    [Fact]
    public void ToggleDone_preserves_all_other_fields_of_the_toggled_task()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "No tocar", true, false, false, now, now);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.ToggleDone(tasks, task.Id);

        Assert.Equal(task.Id, result.Tasks[0].Id);
        Assert.Equal(task.Title, result.Tasks[0].Title);
        Assert.Equal(task.Urgent, result.Tasks[0].Urgent);
        Assert.Equal(task.Important, result.Tasks[0].Important);
        Assert.Equal(task.CreatedAt, result.Tasks[0].CreatedAt);
    }

    [Fact]
    public void ToggleDone_unknown_id_returns_list_unchanged()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = new List<TaskItem>
        {
            new(Guid.NewGuid(), "Tarea", true, true, false, now, now),
        };

        var result = TaskStore.ToggleDone(tasks, Guid.NewGuid());

        Assert.Equal(tasks, result.Tasks);
    }

    [Fact]
    public void ToggleDone_only_affects_the_matching_task_in_a_multi_item_list()
    {
        var now = DateTimeOffset.UtcNow;
        var target = new TaskItem(Guid.NewGuid(), "Objetivo", true, true, false, now, now);
        var other = new TaskItem(Guid.NewGuid(), "Otra", false, true, false, now, now);
        var tasks = new List<TaskItem> { target, other };

        var result = TaskStore.ToggleDone(tasks, target.Id);

        Assert.Equal(2, result.Tasks.Count);
        Assert.True(result.Tasks.Single(t => t.Id == target.Id).Done);
        Assert.False(result.Tasks.Single(t => t.Id == other.Id).Done);
    }

    [Fact]
    public void ToggleDone_json_round_trips_the_flipped_state()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Tarea", true, true, false, now, now);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.ToggleDone(tasks, task.Id);
        var reloaded = TaskStore.Load(result.Json);

        Assert.Equal(LoadStatus.Ok, reloaded.Status);
        Assert.True(reloaded.Tasks.Single(t => t.Id == task.Id).Done);
    }

    // -- Remove (pure, in-memory list) ---------------------------------------------

    [Fact]
    public void Remove_deletes_the_matching_task_and_keeps_the_rest()
    {
        var now = DateTimeOffset.UtcNow;
        var toRemove = new TaskItem(Guid.NewGuid(), "Eliminar", true, true, false, now, now);
        var toKeep = new TaskItem(Guid.NewGuid(), "Mantener", false, true, false, now, now);
        var tasks = new List<TaskItem> { toRemove, toKeep };

        var result = TaskStore.Remove(tasks, toRemove.Id);

        Assert.Single(result.Tasks);
        Assert.Equal(toKeep, result.Tasks[0]);
    }

    [Fact]
    public void Remove_drops_count_by_exactly_one()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = new List<TaskItem>
        {
            new(Guid.NewGuid(), "Uno", true, true, false, now, now),
            new(Guid.NewGuid(), "Dos", false, true, false, now, now),
            new(Guid.NewGuid(), "Tres", true, false, false, now, now),
        };

        var result = TaskStore.Remove(tasks, tasks[1].Id);

        Assert.Equal(tasks.Count - 1, result.Tasks.Count);
    }

    [Fact]
    public void Remove_unknown_id_returns_list_unchanged()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = new List<TaskItem>
        {
            new(Guid.NewGuid(), "Tarea", true, true, false, now, now),
        };

        var result = TaskStore.Remove(tasks, Guid.NewGuid());

        Assert.Equal(tasks, result.Tasks);
    }

    [Fact]
    public void Remove_json_round_trips_to_an_empty_list_when_last_task_removed()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Única", true, true, false, now, now);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.Remove(tasks, task.Id);
        var reloaded = TaskStore.Load(result.Json);

        Assert.Equal(LoadStatus.Ok, reloaded.Status);
        Assert.Empty(reloaded.Tasks);
    }

    // -- SetClassification (pure, in-memory list) -----------------------------------

    [Fact]
    public void SetClassification_updates_urgent_and_important_flags()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Tarea", false, false, false, now, now);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.SetClassification(tasks, task.Id, urgent: true, important: true);

        Assert.Single(result.Tasks);
        Assert.True(result.Tasks[0].Urgent);
        Assert.True(result.Tasks[0].Important);
    }

    [Fact]
    public void SetClassification_bumps_updatedAt()
    {
        var created = DateTimeOffset.UtcNow.AddMinutes(-10);
        var task = new TaskItem(Guid.NewGuid(), "Tarea", false, false, false, created, created);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.SetClassification(tasks, task.Id, urgent: true, important: false);

        Assert.True(result.Tasks[0].UpdatedAt > created);
    }

    [Fact]
    public void SetClassification_moves_task_from_Planificar_to_HacerAhora_when_urgent_flips_true()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Tarea", false, true, false, now, now);
        var tasks = new List<TaskItem> { task };
        Assert.Equal("Planificar", task.Quadrant);

        var result = TaskStore.SetClassification(tasks, task.Id, urgent: true, important: true);

        Assert.Equal("Hacer ahora", result.Tasks[0].Quadrant);
    }

    [Fact]
    public void SetClassification_moves_task_from_HacerAhora_to_Delegar_when_important_flips_false()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Tarea", true, true, false, now, now);
        var tasks = new List<TaskItem> { task };
        Assert.Equal("Hacer ahora", task.Quadrant);

        var result = TaskStore.SetClassification(tasks, task.Id, urgent: true, important: false);

        Assert.Equal("Delegar", result.Tasks[0].Quadrant);
    }

    [Fact]
    public void SetClassification_unknown_id_returns_list_unchanged()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = new List<TaskItem>
        {
            new(Guid.NewGuid(), "Tarea", true, true, false, now, now),
        };

        var result = TaskStore.SetClassification(tasks, Guid.NewGuid(), urgent: false, important: false);

        Assert.Equal(tasks, result.Tasks);
    }

    [Fact]
    public void SetClassification_preserves_all_other_fields_of_the_target_task()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "No tocar", false, false, true, now, now);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.SetClassification(tasks, task.Id, urgent: true, important: true);

        Assert.Equal(task.Id, result.Tasks[0].Id);
        Assert.Equal(task.Title, result.Tasks[0].Title);
        Assert.Equal(task.Done, result.Tasks[0].Done);
        Assert.Equal(task.CreatedAt, result.Tasks[0].CreatedAt);
    }

    [Fact]
    public void SetClassification_only_affects_the_matching_task_in_a_multi_item_list()
    {
        var now = DateTimeOffset.UtcNow;
        var target = new TaskItem(Guid.NewGuid(), "Objetivo", false, false, false, now, now);
        var other = new TaskItem(Guid.NewGuid(), "Otra", false, true, false, now, now);
        var tasks = new List<TaskItem> { target, other };

        var result = TaskStore.SetClassification(tasks, target.Id, urgent: true, important: true);

        Assert.Equal(2, result.Tasks.Count);
        var updatedTarget = result.Tasks.Single(t => t.Id == target.Id);
        Assert.True(updatedTarget.Urgent);
        Assert.True(updatedTarget.Important);
        var untouchedOther = result.Tasks.Single(t => t.Id == other.Id);
        Assert.Equal(other, untouchedOther);
    }

    [Fact]
    public void SetClassification_json_round_trips_the_new_classification()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Tarea", false, false, false, now, now);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.SetClassification(tasks, task.Id, urgent: true, important: false);
        var reloaded = TaskStore.Load(result.Json);

        Assert.Equal(LoadStatus.Ok, reloaded.Status);
        var reloadedTask = reloaded.Tasks.Single(t => t.Id == task.Id);
        Assert.True(reloadedTask.Urgent);
        Assert.False(reloadedTask.Important);
    }

    // -- Add (pure, in-memory list — shared building block with Capture) ------------

    [Fact]
    public void Add_builds_a_TaskItem_and_appends_it_to_the_list()
    {
        var tasks = new List<TaskItem>();

        var result = TaskStore.Add(tasks, "Comprar leche", urgent: true, important: false);

        Assert.True(result.Ok);
        Assert.NotNull(result.Task);
        Assert.NotEqual(Guid.Empty, result.Task!.Id);
        Assert.Equal("Comprar leche", result.Task.Title);
        Assert.True(result.Task.Urgent);
        Assert.False(result.Task.Important);
        Assert.False(result.Task.Done);
        Assert.Equal(result.Task.CreatedAt, result.Task.UpdatedAt);

        var reloaded = TaskStore.Load(result.Json);
        Assert.Equal(LoadStatus.Ok, reloaded.Status);
        Assert.Single(reloaded.Tasks);
        Assert.Equal(result.Task, reloaded.Tasks[0]);
    }

    [Fact]
    public void Add_appends_to_an_already_populated_list_without_touching_existing_tasks()
    {
        var now = DateTimeOffset.UtcNow;
        var existing = new List<TaskItem>
        {
            new(Guid.NewGuid(), "Existente", true, true, false, now, now),
        };

        var result = TaskStore.Add(existing, "Nueva", urgent: false, important: false);

        Assert.True(result.Ok);
        Assert.Equal(2, result.Tasks.Count);
        Assert.Equal(existing[0], result.Tasks[0]);
        Assert.Equal("Nueva", result.Tasks[1].Title);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Add_rejects_blank_or_whitespace_title(string? title)
    {
        var tasks = new List<TaskItem>();

        var result = TaskStore.Add(tasks, title, urgent: true, important: true);

        Assert.False(result.Ok);
        Assert.Null(result.Task);
        Assert.Null(result.Json);
    }

    [Fact]
    public void Capture_still_works_after_being_refactored_onto_the_shared_Add_path()
    {
        var current = new LoadResult(LoadStatus.Empty, Array.Empty<TaskItem>());

        var result = TaskStore.Capture(current, "Tarea compartida", urgent: false, important: true);

        Assert.Equal(CaptureStatus.Ok, result.Status);
        Assert.Equal("Tarea compartida", result.Task!.Title);
        Assert.NotNull(result.Json);
    }

    // -- SetTitle (pure, in-memory list) --------------------------------------------

    [Fact]
    public void SetTitle_updates_the_title_and_bumps_updatedAt()
    {
        var created = DateTimeOffset.UtcNow.AddMinutes(-10);
        var task = new TaskItem(Guid.NewGuid(), "Titulo viejo", true, true, false, created, created);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.SetTitle(tasks, task.Id, "Titulo nuevo");

        Assert.Single(result.Tasks);
        Assert.Equal("Titulo nuevo", result.Tasks[0].Title);
        Assert.True(result.Tasks[0].UpdatedAt > created);
    }

    [Fact]
    public void SetTitle_preserves_all_other_fields_of_the_target_task()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "No tocar", true, false, true, now, now);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.SetTitle(tasks, task.Id, "Titulo nuevo");

        Assert.Equal(task.Id, result.Tasks[0].Id);
        Assert.Equal(task.Urgent, result.Tasks[0].Urgent);
        Assert.Equal(task.Important, result.Tasks[0].Important);
        Assert.Equal(task.Done, result.Tasks[0].Done);
        Assert.Equal(task.CreatedAt, result.Tasks[0].CreatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetTitle_rejects_blank_or_whitespace_title_and_leaves_task_unchanged(string? title)
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Titulo original", true, true, false, now, now);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.SetTitle(tasks, task.Id, title);

        Assert.Single(result.Tasks);
        Assert.Equal("Titulo original", result.Tasks[0].Title);
        Assert.Equal(task.UpdatedAt, result.Tasks[0].UpdatedAt);
    }

    [Fact]
    public void SetTitle_unknown_id_returns_list_unchanged()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = new List<TaskItem>
        {
            new(Guid.NewGuid(), "Tarea", true, true, false, now, now),
        };

        var result = TaskStore.SetTitle(tasks, Guid.NewGuid(), "Otro titulo");

        Assert.Equal(tasks, result.Tasks);
    }

    [Fact]
    public void SetTitle_only_affects_the_matching_task_in_a_multi_item_list()
    {
        var now = DateTimeOffset.UtcNow;
        var target = new TaskItem(Guid.NewGuid(), "Objetivo", false, false, false, now, now);
        var other = new TaskItem(Guid.NewGuid(), "Otra", false, true, false, now, now);
        var tasks = new List<TaskItem> { target, other };

        var result = TaskStore.SetTitle(tasks, target.Id, "Objetivo editado");

        Assert.Equal(2, result.Tasks.Count);
        Assert.Equal("Objetivo editado", result.Tasks.Single(t => t.Id == target.Id).Title);
        Assert.Equal(other, result.Tasks.Single(t => t.Id == other.Id));
    }

    [Fact]
    public void SetTitle_json_round_trips_the_new_title()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Titulo original", true, true, false, now, now);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.SetTitle(tasks, task.Id, "Titulo persistido");
        var reloaded = TaskStore.Load(result.Json);

        Assert.Equal(LoadStatus.Ok, reloaded.Status);
        Assert.Equal("Titulo persistido", reloaded.Tasks.Single(t => t.Id == task.Id).Title);
    }

    // -- Schema v2 backward-compat (linkPath) ---------------------------------------

    [Fact]
    public void TaskItem_LinkPath_defaults_to_null_when_not_specified()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Tarea", true, true, false, now, now);

        Assert.Null(task.LinkPath);
    }

    [Fact]
    public void Load_v1_file_without_linkPath_field_still_loads_Ok_with_null_LinkPath()
    {
        // Documento real emitido por una versión anterior del plugin (schema v1):
        // sin el campo "linkPath" en absoluto.
        const string rawV1 = """
        {
          "version": 1,
          "tasks": [
            {
              "id": "5b2c1d3a-1111-4a2b-9c3d-000000000001",
              "title": "Tarea vieja",
              "urgent": true,
              "important": false,
              "done": false,
              "createdAt": "2026-01-01T00:00:00+00:00",
              "updatedAt": "2026-01-01T00:00:00+00:00"
            }
          ]
        }
        """;

        var result = TaskStore.Load(rawV1);

        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.Single(result.Tasks);
        Assert.Equal("Tarea vieja", result.Tasks[0].Title);
        Assert.Null(result.Tasks[0].LinkPath);
    }

    [Fact]
    public void Load_v2_file_with_linkPath_round_trips_the_value()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Tarea con link", true, true, false, now, now, "notas/referencia.md");
        var tasks = new List<TaskItem> { task };

        var json = TaskStore.Serialize(tasks);
        var reloaded = TaskStore.Load(json);

        Assert.Equal(LoadStatus.Ok, reloaded.Status);
        Assert.Equal("notas/referencia.md", reloaded.Tasks[0].LinkPath);
    }

    // -- SetLink (pure, in-memory list) ----------------------------------------------

    [Fact]
    public void SetLink_sets_the_link_path_and_bumps_updatedAt()
    {
        var created = DateTimeOffset.UtcNow.AddMinutes(-10);
        var task = new TaskItem(Guid.NewGuid(), "Tarea", true, true, false, created, created);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.SetLink(tasks, task.Id, "notas/referencia.md");

        Assert.Single(result.Tasks);
        Assert.Equal("notas/referencia.md", result.Tasks[0].LinkPath);
        Assert.True(result.Tasks[0].UpdatedAt > created);
    }

    [Fact]
    public void SetLink_null_clears_an_existing_link_path()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Tarea", true, true, false, now, now, "notas/referencia.md");
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.SetLink(tasks, task.Id, null);

        Assert.Null(result.Tasks[0].LinkPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetLink_blank_or_whitespace_also_clears_the_link_path(string blank)
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Tarea", true, true, false, now, now, "notas/referencia.md");
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.SetLink(tasks, task.Id, blank);

        Assert.Null(result.Tasks[0].LinkPath);
    }

    [Fact]
    public void SetLink_unknown_id_returns_list_unchanged()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = new List<TaskItem>
        {
            new(Guid.NewGuid(), "Tarea", true, true, false, now, now),
        };

        var result = TaskStore.SetLink(tasks, Guid.NewGuid(), "notas/referencia.md");

        Assert.Equal(tasks, result.Tasks);
    }

    [Fact]
    public void SetLink_preserves_all_other_fields_of_the_target_task()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "No tocar", true, false, true, now, now);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.SetLink(tasks, task.Id, "notas/referencia.md");

        Assert.Equal(task.Id, result.Tasks[0].Id);
        Assert.Equal(task.Title, result.Tasks[0].Title);
        Assert.Equal(task.Urgent, result.Tasks[0].Urgent);
        Assert.Equal(task.Important, result.Tasks[0].Important);
        Assert.Equal(task.Done, result.Tasks[0].Done);
        Assert.Equal(task.CreatedAt, result.Tasks[0].CreatedAt);
    }

    [Fact]
    public void SetLink_only_affects_the_matching_task_in_a_multi_item_list()
    {
        var now = DateTimeOffset.UtcNow;
        var target = new TaskItem(Guid.NewGuid(), "Objetivo", false, false, false, now, now);
        var other = new TaskItem(Guid.NewGuid(), "Otra", false, true, false, now, now);
        var tasks = new List<TaskItem> { target, other };

        var result = TaskStore.SetLink(tasks, target.Id, "notas/referencia.md");

        Assert.Equal(2, result.Tasks.Count);
        Assert.Equal("notas/referencia.md", result.Tasks.Single(t => t.Id == target.Id).LinkPath);
        Assert.Equal(other, result.Tasks.Single(t => t.Id == other.Id));
    }

    [Fact]
    public void SetLink_json_round_trips_the_link_path()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Tarea", true, true, false, now, now);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.SetLink(tasks, task.Id, "notas/referencia.md");
        var reloaded = TaskStore.Load(result.Json);

        Assert.Equal(LoadStatus.Ok, reloaded.Status);
        Assert.Equal("notas/referencia.md", reloaded.Tasks.Single(t => t.Id == task.Id).LinkPath);
    }

    // -- Active / Completed (pure — history-list split for the Eisenhower window) ---

    [Fact]
    public void Active_returns_only_tasks_that_are_not_done()
    {
        var now = DateTimeOffset.UtcNow;
        var pending = new TaskItem(Guid.NewGuid(), "Pendiente", true, true, false, now, now);
        var done = new TaskItem(Guid.NewGuid(), "Hecha", true, true, true, now, now);
        var tasks = new List<TaskItem> { pending, done };

        var result = TaskStore.Active(tasks);

        Assert.Single(result);
        Assert.Equal(pending.Id, result[0].Id);
    }

    [Fact]
    public void Active_returns_empty_list_when_all_tasks_done()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = new List<TaskItem>
        {
            new(Guid.NewGuid(), "Hecha 1", true, true, true, now, now),
            new(Guid.NewGuid(), "Hecha 2", false, true, true, now, now),
        };

        var result = TaskStore.Active(tasks);

        Assert.Empty(result);
    }

    [Fact]
    public void Active_returns_empty_list_for_empty_input()
    {
        var result = TaskStore.Active(Array.Empty<TaskItem>());

        Assert.Empty(result);
    }

    [Fact]
    public void Active_preserves_relative_order_of_pending_tasks()
    {
        var now = DateTimeOffset.UtcNow;
        var first = new TaskItem(Guid.NewGuid(), "Primera", true, true, false, now, now);
        var doneInBetween = new TaskItem(Guid.NewGuid(), "Hecha", true, true, true, now, now);
        var second = new TaskItem(Guid.NewGuid(), "Segunda", false, true, false, now, now);
        var tasks = new List<TaskItem> { first, doneInBetween, second };

        var result = TaskStore.Active(tasks);

        Assert.Equal(2, result.Count);
        Assert.Equal(first.Id, result[0].Id);
        Assert.Equal(second.Id, result[1].Id);
    }

    [Fact]
    public void Completed_returns_only_tasks_that_are_done()
    {
        var now = DateTimeOffset.UtcNow;
        var pending = new TaskItem(Guid.NewGuid(), "Pendiente", true, true, false, now, now);
        var done = new TaskItem(Guid.NewGuid(), "Hecha", true, true, true, now, now);
        var tasks = new List<TaskItem> { pending, done };

        var result = TaskStore.Completed(tasks);

        Assert.Single(result);
        Assert.Equal(done.Id, result[0].Id);
    }

    [Fact]
    public void Completed_returns_empty_list_when_no_tasks_done()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = new List<TaskItem>
        {
            new(Guid.NewGuid(), "Pendiente 1", true, true, false, now, now),
            new(Guid.NewGuid(), "Pendiente 2", false, true, false, now, now),
        };

        var result = TaskStore.Completed(tasks);

        Assert.Empty(result);
    }

    [Fact]
    public void Completed_returns_empty_list_for_empty_input()
    {
        var result = TaskStore.Completed(Array.Empty<TaskItem>());

        Assert.Empty(result);
    }

    [Fact]
    public void Completed_sorts_by_updatedAt_descending_most_recently_completed_first()
    {
        var now = DateTimeOffset.UtcNow;
        var oldest = new TaskItem(Guid.NewGuid(), "Completada hace 3 dias", true, true, true, now, now.AddDays(-3));
        var newest = new TaskItem(Guid.NewGuid(), "Completada recien", true, true, true, now, now);
        var middle = new TaskItem(Guid.NewGuid(), "Completada ayer", true, true, true, now, now.AddDays(-1));
        var tasks = new List<TaskItem> { oldest, middle, newest };

        var result = TaskStore.Completed(tasks);

        Assert.Equal(3, result.Count);
        Assert.Equal(newest.Id, result[0].Id);
        Assert.Equal(middle.Id, result[1].Id);
        Assert.Equal(oldest.Id, result[2].Id);
    }

    [Fact]
    public void Active_and_Completed_partition_all_tasks_without_overlap()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = new List<TaskItem>
        {
            new(Guid.NewGuid(), "Uno", true, true, false, now, now),
            new(Guid.NewGuid(), "Dos", false, true, true, now, now),
            new(Guid.NewGuid(), "Tres", true, false, false, now, now),
            new(Guid.NewGuid(), "Cuatro", false, false, true, now, now),
        };

        var active = TaskStore.Active(tasks);
        var completed = TaskStore.Completed(tasks);

        Assert.Equal(2, active.Count);
        Assert.Equal(2, completed.Count);
        Assert.Empty(active.Select(t => t.Id).Intersect(completed.Select(t => t.Id)));
        Assert.Equal(tasks.Count, active.Count + completed.Count);
    }

    // -- Schema v3 (completedAt) -----------------------------------------------------

    [Fact]
    public void SchemaVersion_is_now_3()
    {
        Assert.Equal(3, TaskStore.SchemaVersion);
    }

    [Fact]
    public void TaskItem_CompletedAt_defaults_to_null_when_not_specified()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Tarea", true, true, false, now, now);

        Assert.Null(task.CompletedAt);
    }

    [Fact]
    public void Load_v2_file_without_completedAt_field_still_loads_Ok_with_null_CompletedAt()
    {
        // Documento real emitido por schema v2 (con linkPath, sin completedAt).
        const string rawV2 = """
        {
          "version": 2,
          "tasks": [
            {
              "id": "5b2c1d3a-2222-4a2b-9c3d-000000000002",
              "title": "Tarea v2",
              "urgent": true,
              "important": false,
              "done": true,
              "createdAt": "2026-01-01T00:00:00+00:00",
              "updatedAt": "2026-01-05T00:00:00+00:00",
              "linkPath": "notas/vieja.md"
            }
          ]
        }
        """;

        var result = TaskStore.Load(rawV2);

        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.Single(result.Tasks);
        Assert.Equal("notas/vieja.md", result.Tasks[0].LinkPath);
        Assert.Null(result.Tasks[0].CompletedAt);
    }

    [Fact]
    public void Serialize_then_Load_round_trips_completedAt_when_set()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Tarea completada", true, true, true, now, now, null, now);
        var tasks = new List<TaskItem> { task };

        var json = TaskStore.Serialize(tasks);
        var reloaded = TaskStore.Load(json);

        Assert.Equal(LoadStatus.Ok, reloaded.Status);
        Assert.Equal(task.CompletedAt, reloaded.Tasks[0].CompletedAt);
    }

    // -- ToggleDone sets/clears CompletedAt -------------------------------------------

    [Fact]
    public void ToggleDone_sets_completedAt_when_task_becomes_done()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Tarea", true, true, false, now, now);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.ToggleDone(tasks, task.Id);

        Assert.True(result.Tasks[0].Done);
        Assert.NotNull(result.Tasks[0].CompletedAt);
    }

    [Fact]
    public void ToggleDone_clears_completedAt_when_task_becomes_not_done()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "Tarea", true, true, true, now, now, null, now);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.ToggleDone(tasks, task.Id);

        Assert.False(result.Tasks[0].Done);
        Assert.Null(result.Tasks[0].CompletedAt);
    }

    // -- CompletedByMonth (pure grouping for the tabbed history UI) -------------------

    [Fact]
    public void CompletedByMonth_returns_empty_for_no_tasks()
    {
        var result = TaskStore.CompletedByMonth(Array.Empty<TaskItem>());

        Assert.Empty(result);
    }

    [Fact]
    public void CompletedByMonth_excludes_active_tasks()
    {
        var now = DateTimeOffset.UtcNow;
        var active = new TaskItem(Guid.NewGuid(), "Pendiente", true, true, false, now, now);
        var tasks = new List<TaskItem> { active };

        var result = TaskStore.CompletedByMonth(tasks);

        Assert.Empty(result);
    }

    [Fact]
    public void CompletedByMonth_groups_tasks_completed_in_the_same_month_together()
    {
        var completedAt = new DateTimeOffset(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);
        var a = new TaskItem(Guid.NewGuid(), "A", true, true, true, completedAt, completedAt, null, completedAt);
        var b = new TaskItem(Guid.NewGuid(), "B", false, true, true, completedAt, completedAt, null, completedAt.AddDays(1));
        var tasks = new List<TaskItem> { a, b };

        var result = TaskStore.CompletedByMonth(tasks);

        Assert.Single(result);
        Assert.Equal(2, result[0].Tasks.Count);
    }

    [Fact]
    public void CompletedByMonth_sorts_groups_most_recent_month_first()
    {
        var june = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var july = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var august = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        var taskJune = new TaskItem(Guid.NewGuid(), "Junio", true, true, true, june, june, null, june);
        var taskAugust = new TaskItem(Guid.NewGuid(), "Agosto", true, true, true, august, august, null, august);
        var taskJuly = new TaskItem(Guid.NewGuid(), "Julio", true, true, true, july, july, null, july);
        var tasks = new List<TaskItem> { taskJune, taskAugust, taskJuly };

        var result = TaskStore.CompletedByMonth(tasks);

        Assert.Equal(3, result.Count);
        Assert.Equal(2026, result[0].Year);
        Assert.Equal(8, result[0].Month);
        Assert.Equal(7, result[1].Month);
        Assert.Equal(6, result[2].Month);
    }

    [Fact]
    public void CompletedByMonth_sorts_tasks_within_a_group_by_completion_date_descending()
    {
        var older = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var taskOlder = new TaskItem(Guid.NewGuid(), "Vieja", true, true, true, older, older, null, older);
        var taskNewer = new TaskItem(Guid.NewGuid(), "Nueva", true, true, true, newer, newer, null, newer);
        var tasks = new List<TaskItem> { taskOlder, taskNewer };

        var result = TaskStore.CompletedByMonth(tasks);

        Assert.Equal("Nueva", result[0].Tasks[0].Title);
        Assert.Equal("Vieja", result[0].Tasks[1].Title);
    }

    [Fact]
    public void CompletedByMonth_label_is_spanish_month_name_and_year_capitalized()
    {
        var completedAt = new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero);
        var task = new TaskItem(Guid.NewGuid(), "Tarea", true, true, true, completedAt, completedAt, null, completedAt);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.CompletedByMonth(tasks);

        Assert.Equal("Agosto 2026", result[0].Label);
    }

    [Theory]
    [InlineData(1, "Enero")]
    [InlineData(2, "Febrero")]
    [InlineData(3, "Marzo")]
    [InlineData(4, "Abril")]
    [InlineData(5, "Mayo")]
    [InlineData(6, "Junio")]
    [InlineData(7, "Julio")]
    [InlineData(8, "Agosto")]
    [InlineData(9, "Septiembre")]
    [InlineData(10, "Octubre")]
    [InlineData(11, "Noviembre")]
    [InlineData(12, "Diciembre")]
    public void CompletedByMonth_label_uses_fixed_spanish_month_names_for_all_months(int month, string expectedMonthName)
    {
        var completedAt = new DateTimeOffset(2026, month, 1, 0, 0, 0, TimeSpan.Zero);
        var task = new TaskItem(Guid.NewGuid(), "Tarea", true, true, true, completedAt, completedAt, null, completedAt);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.CompletedByMonth(tasks);

        Assert.Equal($"{expectedMonthName} 2026", result[0].Label);
    }

    [Fact]
    public void CompletedByMonth_falls_back_to_updatedAt_when_completedAt_is_null()
    {
        // Tarea completada bajo schema v2 (antes de que existiera completedAt):
        // Done=true, CompletedAt=null — el fallback usa UpdatedAt para agrupar.
        var updatedAt = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);
        var task = new TaskItem(Guid.NewGuid(), "Tarea vieja completada", true, true, true,
            updatedAt.AddMonths(-2), updatedAt);
        var tasks = new List<TaskItem> { task };

        var result = TaskStore.CompletedByMonth(tasks);

        Assert.Single(result);
        Assert.Equal(2026, result[0].Year);
        Assert.Equal(3, result[0].Month);
        Assert.Equal("Marzo 2026", result[0].Label);
    }
}
