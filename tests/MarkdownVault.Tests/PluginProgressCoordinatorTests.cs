using MarkdownVault.PluginSdk;
using MarkdownVault.Services.Plugins;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Cubre <see cref="PluginProgressCoordinator"/>: la pila LIFO de scopes de
/// progreso, el conteo de "otros" que quedan atrás, el barrido forzado por dueño
/// (desactivación de un plugin) y el botón «Cancelar» de dos tiempos.
///
/// Casos portados desde el port a Python verificado en el scratchpad
/// (<c>verify_progress.py</c>) contra la lógica REAL — ver architecture/sdk-canal-
/// de-progreso. El <c>marshal</c> que en producción es <c>Dispatcher.BeginInvoke</c>
/// se inyecta como <c>a =&gt; a()</c> para que todo quede síncrono en las pruebas
/// (mismo criterio que <c>uiDispatch: a =&gt; a()</c> en PluginManagerTests). Las
/// funciones puras internas (<see cref="PluginProgressCoordinator.NormalizePercent"/>,
/// <see cref="PluginProgressCoordinator.Normalize"/>) son visibles acá vía
/// <c>InternalsVisibleTo</c> (MarkdownVault.csproj).
/// </summary>
public class PluginProgressCoordinatorTests
{
    private static PluginProgressCoordinator NewCoordinator(List<ProgressSnapshot?> posts)
    {
        var coordinator = new PluginProgressCoordinator(marshal: a => a());
        coordinator.ActiveChanged += s => posts.Add(s);
        return coordinator;
    }

    // ── Funciones puras: recorte de porcentaje y normalización de mensaje ───

    [Fact]
    public void NormalizePercent_of_null_is_indeterminate()
    {
        Assert.Null(PluginProgressCoordinator.NormalizePercent(null));
    }

    [Fact]
    public void NormalizePercent_of_nan_or_infinite_is_indeterminate_and_does_not_throw()
    {
        Assert.Null(PluginProgressCoordinator.NormalizePercent(double.NaN));
        Assert.Null(PluginProgressCoordinator.NormalizePercent(double.PositiveInfinity));
        Assert.Null(PluginProgressCoordinator.NormalizePercent(double.NegativeInfinity));
    }

    [Theory]
    [InlineData(-5.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(42.7, 42.7)]
    [InlineData(100.0, 100.0)]
    [InlineData(1000.0, 100.0)]
    public void NormalizePercent_clamps_to_the_0_100_range(double input, double expected)
    {
        Assert.Equal(expected, PluginProgressCoordinator.NormalizePercent(input));
    }

    [Fact]
    public void Normalize_of_a_blank_message_keeps_the_previous_one()
    {
        Assert.Equal("previo", PluginProgressCoordinator.Normalize("", "previo"));
        Assert.Equal("previo", PluginProgressCoordinator.Normalize("   ", "previo"));
        Assert.Equal("previo", PluginProgressCoordinator.Normalize(null, "previo"));
    }

    [Fact]
    public void Normalize_trims_the_message()
    {
        Assert.Equal("hola", PluginProgressCoordinator.Normalize("  hola  ", ""));
    }

    [Fact]
    public void Normalize_truncates_long_messages_with_an_ellipsis()
    {
        var largo = new string('x', 300);

        var result = PluginProgressCoordinator.Normalize(largo, "");

        Assert.Equal(PluginProgressCoordinator.MaxMessageLength + 1, result.Length);
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void Blank_title_falls_back_to_a_generic_default()
    {
        var posts = new List<ProgressSnapshot?>();
        var coordinator = NewCoordinator(posts);

        coordinator.Begin("p", "   ");

        Assert.Equal("Trabajando…", posts[0]!.Title);
    }

    // ── Ciclo de vida de un scope ────────────────────────────────────────────

    [Fact]
    public void Begin_shows_the_scope_report_updates_it_and_dispose_hides_it()
    {
        var posts = new List<ProgressSnapshot?>();
        var coordinator = NewCoordinator(posts);

        var scope = coordinator.Begin("core.dictado-voz", "Dictado de voz");
        scope.Report(10, "Descargando…");
        scope.Dispose();

        Assert.Equal(new ProgressSnapshot("Dictado de voz", "", null, 0, false), posts[0]);
        Assert.Equal(new ProgressSnapshot("Dictado de voz", "Descargando…", 10.0, 0, false), posts[1]);
        Assert.Null(posts[2]);
        Assert.Equal(3, posts.Count);
    }

    // ── Pila LIFO ─────────────────────────────────────────────────────────────

    [Fact]
    public void Most_recent_scope_wins_and_the_previous_one_reappears_when_it_closes()
    {
        var posts = new List<ProgressSnapshot?>();
        var coordinator = NewCoordinator(posts);

        var a = coordinator.Begin("plugin.a", "A");
        a.Report(30, "paso A");
        var b = coordinator.Begin("plugin.b", "B");
        b.Report(70, "paso B");

        Assert.Equal(new ProgressSnapshot("B", "paso B", 70.0, 1, false), posts[^1]);

        b.Dispose();
        Assert.Equal(new ProgressSnapshot("A", "paso A", 30.0, 0, false), posts[^1]);

        a.Dispose();
        Assert.Null(posts[^1]);
    }

    [Fact]
    public void OtherCount_counts_the_scopes_left_behind_in_the_stack()
    {
        var posts = new List<ProgressSnapshot?>();
        var coordinator = NewCoordinator(posts);

        coordinator.Begin("p", "A");
        coordinator.Begin("p", "B");
        var top = coordinator.Begin("p", "C");
        Assert.Equal(2, posts[^1]!.OtherCount);

        top.Dispose();
        Assert.Equal(1, posts[^1]!.OtherCount);
    }

    /// <summary>El caso real que motivó la pila: transcripción → arranque del
    /// motor → descarga del modelo. Se ve SIEMPRE el paso de más adentro, que es
    /// el que realmente avanza.</summary>
    [Fact]
    public void Nested_scopes_of_the_same_plugin_show_the_innermost_one()
    {
        var posts = new List<ProgressSnapshot?>();
        var coordinator = NewCoordinator(posts);

        var outer = coordinator.Begin("core.dictado-voz", "Dictado de voz");
        outer.Report(null, "Esperando al motor…");
        var inner = coordinator.Begin("core.dictado-voz", "Dictado de voz");
        inner.Report(3, "Descargando el modelo (547 MB)…");

        Assert.StartsWith("Descargando", posts[^1]!.Message);

        inner.Dispose();
        Assert.Equal("Esperando al motor…", posts[^1]!.Message);
    }

    // ── Barrido por dueño (desactivación de un plugin) ──────────────────────

    [Fact]
    public void Deactivating_a_plugin_closes_and_cancels_only_its_own_scopes()
    {
        var coordinator = new PluginProgressCoordinator(marshal: a => a());
        var mine1 = coordinator.Begin("core.dictado-voz", "Dictado");
        var other = coordinator.Begin("core.eisenhower", "Eisenhower");
        var mine2 = coordinator.Begin("core.dictado-voz", "Dictado 2");

        coordinator.CloseAllFor("core.dictado-voz");

        Assert.True(mine1.IsCancellationRequested);
        Assert.True(mine2.IsCancellationRequested);
        Assert.False(other.IsCancellationRequested);
        Assert.Equal(1, coordinator.ActiveCount);
    }

    [Fact]
    public void Deactivating_matches_the_owner_case_insensitively()
    {
        var coordinator = new PluginProgressCoordinator(marshal: a => a());
        coordinator.Begin("Core.Dictado-Voz", "D");

        coordinator.CloseAllFor("core.dictado-voz");

        Assert.Equal(0, coordinator.ActiveCount);
    }

    // ── Cancelar: dos tiempos ────────────────────────────────────────────────

    [Fact]
    public void Cancel_button_is_two_stage_first_press_cancels_second_discards()
    {
        var posts = new List<ProgressSnapshot?>();
        var coordinator = NewCoordinator(posts);
        var scope = coordinator.Begin("p", "T");

        coordinator.CancelOrDiscardActive();
        Assert.True(scope.IsCancellationRequested);
        Assert.Equal(1, coordinator.ActiveCount);
        Assert.True(posts[^1]!.IsCancelling);

        coordinator.CancelOrDiscardActive();
        Assert.Equal(0, coordinator.ActiveCount);
        Assert.Null(posts[^1]);
    }

    [Fact]
    public void Cancel_with_nothing_active_does_not_throw()
    {
        var coordinator = new PluginProgressCoordinator(marshal: a => a());

        var exception = Record.Exception(() => coordinator.CancelOrDiscardActive());

        Assert.Null(exception);
    }

    // ── No-ops silenciosos después de cerrado ───────────────────────────────

    [Fact]
    public void Report_and_dispose_after_the_scope_is_closed_are_silent_no_ops()
    {
        var posts = new List<ProgressSnapshot?>();
        var coordinator = NewCoordinator(posts);
        var scope = coordinator.Begin("p", "T");
        coordinator.CloseAllFor("p");
        var countAfterClose = posts.Count;

        var exception = Record.Exception(() =>
        {
            scope.Report(50, "tarde");
            scope.Dispose();
        });

        Assert.Null(exception);
        Assert.Equal(countAfterClose, posts.Count);   // ningún reporte nuevo se publicó
        Assert.Equal(0, coordinator.ActiveCount);
    }

    // ── Fusión de ráfagas: deduplicación del snapshot publicado ─────────────

    [Fact]
    public void Reporting_the_same_state_repeatedly_does_not_republish()
    {
        var posts = new List<ProgressSnapshot?>();
        var coordinator = NewCoordinator(posts);
        var scope = coordinator.Begin("p", "T");

        scope.Report(42, "igual");
        var countAfterFirst = posts.Count;

        for (var i = 0; i < 50; i++)
            scope.Report(42, "igual");

        Assert.Equal(countAfterFirst, posts.Count);
    }

    // ── Las dos sobrecargas de Report ────────────────────────────────────────

    [Fact]
    public void Message_only_report_keeps_the_percent_but_a_null_percent_report_clears_it()
    {
        var posts = new List<ProgressSnapshot?>();
        var coordinator = NewCoordinator(posts);
        var scope = coordinator.Begin("p", "T");

        scope.Report(60, "a");
        Assert.Equal(60.0, posts[^1]!.Percent);

        scope.Report("b");
        Assert.Equal(60.0, posts[^1]!.Percent);
        Assert.Equal("b", posts[^1]!.Message);

        scope.Report(null, "c");
        Assert.Null(posts[^1]!.Percent);
        Assert.Equal("c", posts[^1]!.Message);
    }

    [Fact]
    public void Report_with_an_empty_message_moves_the_bar_without_erasing_the_text()
    {
        var posts = new List<ProgressSnapshot?>();
        var coordinator = NewCoordinator(posts);
        var scope = coordinator.Begin("p", "T");

        scope.Report(10, "Descargando…");
        scope.Report(20, "");

        Assert.Equal("Descargando…", posts[^1]!.Message);
        Assert.Equal(20.0, posts[^1]!.Percent);
    }
}
