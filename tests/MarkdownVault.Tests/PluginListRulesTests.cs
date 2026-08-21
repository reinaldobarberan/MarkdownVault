using MarkdownVault.PluginSdk;
using MarkdownVault.Services.Plugins;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Cubre <see cref="PluginListRules"/>, la lógica GENÉRICA de las listas editables
/// que el host dibuja para los plugins (SDK 1.4.0): normalización, detección de
/// duplicados sin distinguir mayúsculas, filtro de búsqueda y saneado previo al
/// guardado.
///
/// Es la parte que decide si el usuario puede o no guardar, así que se prueba sin
/// WPF: el ViewModel es una cáscara encima de esto. Los casos están portados del
/// port a Python verificado en el scratchpad antes de escribir el C#
/// (mismo criterio que TranscriptFormatter y TechnicalGlossary).
/// </summary>
public class PluginListRulesTests
{
    private static PluginListEntry E(string key, string? value = null) => new(key, value);

    private static readonly IReadOnlyList<PluginListEntry> Base = new[]
    {
        E("C#"), E("pipeline"), E("README")
    };

    // ── Normalización ───────────────────────────────────────────────────────

    [Fact]
    public void NormalizeKey_trims_and_never_returns_null()
    {
        Assert.Equal("pipeline", PluginListRules.NormalizeKey("  pipeline "));
        Assert.Equal("",         PluginListRules.NormalizeKey(null));
    }

    [Fact]
    public void NormalizeValue_collapses_blank_to_null()
    {
        Assert.Null(PluginListRules.NormalizeValue("   "));
        Assert.Null(PluginListRules.NormalizeValue(null));
        Assert.Equal("ci sharp", PluginListRules.NormalizeValue("  ci sharp "));
    }

    [Fact]
    public void Normalize_cleans_both_columns()
    {
        var entry = PluginListRules.Normalize(E("  C# ", "  "));

        Assert.Equal("C#", entry.Key);
        Assert.Null(entry.Value);
    }

    [Fact]
    public void IsBlank_only_looks_at_the_trimmed_key()
    {
        Assert.True(PluginListRules.IsBlank("   "));
        Assert.True(PluginListRules.IsBlank(null));
        Assert.False(PluginListRules.IsBlank(" x "));
    }

    // ── Duplicados ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("pipeline")]
    [InlineData("PIPELINE")]
    [InlineData("  PiPeLiNe  ")]
    public void IndexOfKey_ignores_case_and_surrounding_space(string key)
        => Assert.Equal(1, PluginListRules.IndexOfKey(Base, key));

    [Fact]
    public void IndexOfKey_returns_minus_one_when_absent_or_blank()
    {
        Assert.Equal(-1, PluginListRules.IndexOfKey(Base, "kernel"));
        Assert.Equal(-1, PluginListRules.IndexOfKey(Base, "   "));
    }

    [Fact]
    public void IndexOfKey_skips_the_row_being_edited()
        => Assert.Equal(-1, PluginListRules.IndexOfKey(Base, "pipeline", exceptIndex: 1));

    [Fact]
    public void Accents_are_NOT_folded_away()
    {
        // "publico" y "público" son dos términos distintos en un glosario. Se ignoran
        // las mayúsculas, no los acentos.
        var entries = new[] { E("publico") };

        Assert.Equal(-1, PluginListRules.IndexOfKey(entries, "público"));
    }

    [Fact]
    public void Validate_reports_blank_duplicate_or_nothing()
    {
        Assert.Equal(ListEntryProblem.Blank,     PluginListRules.Validate(Base, "  "));
        Assert.Equal(ListEntryProblem.Duplicate, PluginListRules.Validate(Base, "c#"));
        Assert.Equal(ListEntryProblem.None,      PluginListRules.Validate(Base, "kernel"));
        Assert.Equal(ListEntryProblem.None,      PluginListRules.Validate(Base, "C#", exceptIndex: 0));
    }

    [Fact]
    public void Message_uses_the_label_the_plugin_chose()
    {
        Assert.Equal("«Término» no puede quedar vacío.",
            PluginListRules.Message(ListEntryProblem.Blank, "Término"));
        Assert.Equal("Ya hay una entrada con ese «Término» (no se distinguen mayúsculas).",
            PluginListRules.Message(ListEntryProblem.Duplicate, "Término"));
        Assert.Null(PluginListRules.Message(ListEntryProblem.None, "Término"));
    }

    [Fact]
    public void Message_falls_back_when_the_plugin_left_the_label_empty()
        => Assert.Equal("«La entrada» no puede quedar vacío.",
            PluginListRules.Message(ListEntryProblem.Blank, "  "));

    // ── Filtro ──────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<PluginListEntry> Mixed = new[]
    {
        E("C#", "ci sharp"), E("pipeline"), E("README", "rid mi"), E("Markdown")
    };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_filter_lets_everything_through(string? filter)
        => Assert.Equal(Mixed.Count, PluginListRules.Filter(Mixed, filter).Count);

    [Fact]
    public void Filter_matches_a_case_insensitive_substring_of_the_key()
    {
        var only = Assert.Single(PluginListRules.Filter(Mixed, "MARK"));
        Assert.Equal("Markdown", only.Key);
    }

    [Fact]
    public void Filter_also_looks_at_the_second_column()
    {
        // Importa para las listas de DOS columnas (el diccionario de pronunciación
        // del Lector): buscar por cómo suena tiene que encontrar el término.
        var only = Assert.Single(PluginListRules.Filter(Mixed, "rid"));
        Assert.Equal("README", only.Key);
    }

    [Fact]
    public void Filter_trims_what_the_user_typed()
    {
        var only = Assert.Single(PluginListRules.Filter(Mixed, "  c#  "));
        Assert.Equal("C#", only.Key);
    }

    [Fact]
    public void Filter_can_come_back_empty()
        => Assert.Empty(PluginListRules.Filter(Mixed, "zzz"));

    // ── Saneado ─────────────────────────────────────────────────────────────

    [Fact]
    public void Sanitize_trims_and_drops_entries_without_a_key()
    {
        var result = PluginListRules.Sanitize(new[] { E("  C# "), E("   ", "x"), E("pipeline", "  ") });

        Assert.Collection(result,
            first  => { Assert.Equal("C#", first.Key);        Assert.Null(first.Value);  },
            second => { Assert.Equal("pipeline", second.Key); Assert.Null(second.Value); });
    }

    [Fact]
    public void Sanitize_keeps_the_first_spelling_of_a_repeated_key()
    {
        // Mismo criterio que TechnicalGlossary: gana la primera grafía vista.
        var result = PluginListRules.Sanitize(new[] { E("Pipeline"), E("pipeline", "x") });

        var only = Assert.Single(result);
        Assert.Equal("Pipeline", only.Key);
        Assert.Null(only.Value);
    }

    [Fact]
    public void Sanitize_of_nothing_is_nothing()
        => Assert.Empty(PluginListRules.Sanitize(Array.Empty<PluginListEntry>()));
}
