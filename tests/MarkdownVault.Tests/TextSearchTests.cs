using System.Text.RegularExpressions;
using MarkdownVault.Services;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Cubre el motor de Buscar/Reemplazar (<see cref="TextSearch"/>). Es lógica pura sobre un
/// string — sin AvalonEdit ni WPF de por medio — así que todo el comportamiento que importa
/// (bordes de palabra, wrap, expansión de grupos, patrones de largo cero) se ejercita acá y
/// no en la ventana.
/// </summary>
public class TextSearchTests
{
    private static Regex Build(
        string pattern, bool matchCase = false, bool wholeWord = false, bool useRegex = false)
    {
        var ok = TextSearch.TryBuild(
            pattern, new TextSearchOptions(matchCase, wholeWord, useRegex),
            out var regex, out var error);

        Assert.True(ok, error ?? "TryBuild falló sin mensaje");
        return regex!;
    }

    // ─── Compilación del patrón ──────────────────────────────────────────────

    [Fact]
    public void TryBuild_rejects_an_empty_pattern()
    {
        var ok = TextSearch.TryBuild("", new TextSearchOptions(), out var regex, out var error);

        Assert.False(ok);
        Assert.Null(regex);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryBuild_reports_an_invalid_regex_instead_of_throwing()
    {
        var ok = TextSearch.TryBuild(
            "(sin cerrar", new TextSearchOptions(UseRegex: true), out var regex, out var error);

        Assert.False(ok);
        Assert.Null(regex);
        Assert.Contains("inválida", error!);
    }

    [Fact]
    public void TryBuild_treats_regex_metacharacters_as_literal_when_regex_is_off()
    {
        // Sin escapar, "c.sto" encontraría "costo"; escapado solo encuentra el punto real.
        var matches = TextSearch.FindAll("costo c.sto", Build("c.sto"));

        Assert.Single(matches);
        Assert.Equal(6, matches[0].Offset);
    }

    // ─── Opciones ────────────────────────────────────────────────────────────

    [Fact]
    public void Search_ignores_case_by_default()
    {
        Assert.Equal(2, TextSearch.FindAll("vault Vault", Build("vault")).Count);
    }

    [Fact]
    public void MatchCase_only_finds_the_exact_casing()
    {
        var matches = TextSearch.FindAll("vault Vault", Build("Vault", matchCase: true));

        Assert.Single(matches);
        Assert.Equal(6, matches[0].Offset);
    }

    [Fact]
    public void WholeWord_skips_a_hit_glued_to_more_letters()
    {
        var matches = TextSearch.FindAll("vault vaultroot", Build("vault", wholeWord: true));

        Assert.Single(matches);
        Assert.Equal(0, matches[0].Offset);
    }

    [Fact]
    public void WholeWord_still_matches_a_pattern_made_of_symbols()
    {
        // Con \b esto NO coincidiría: entre un espacio y un "-" no hay borde de palabra.
        // Es exactamente el caso que justifica los lookarounds en TryBuild.
        var matches = TextSearch.FindAll("a -> b", Build("->", wholeWord: true));

        Assert.Single(matches);
        Assert.Equal(2, matches[0].Offset);
    }

    // ─── Recorrido ───────────────────────────────────────────────────────────

    [Fact]
    public void FindNext_returns_the_first_hit_starting_at_or_after_the_offset()
    {
        var hit = TextSearch.FindNext("aXaXa", Build("a"), from: 1);

        Assert.Equal(2, hit!.Value.Offset);
    }

    [Fact]
    public void FindNext_wraps_to_the_top_when_there_is_nothing_left()
    {
        var hit = TextSearch.FindNext("aXaXa", Build("a"), from: 5);

        Assert.Equal(0, hit!.Value.Offset);
    }

    [Fact]
    public void FindNext_without_wrap_reports_the_end_of_the_document()
    {
        Assert.Null(TextSearch.FindNext("aXaXa", Build("a"), from: 5, wrap: false));
    }

    [Fact]
    public void FindPrevious_skips_the_hit_that_is_currently_selected()
    {
        // Con la coincidencia de offset 4 seleccionada, "anterior" debe dar la de offset 2.
        var hit = TextSearch.FindPrevious("aXaXa", Build("a"), before: 4);

        Assert.Equal(2, hit!.Value.Offset);
    }

    [Fact]
    public void FindPrevious_wraps_to_the_last_hit_of_the_document()
    {
        var hit = TextSearch.FindPrevious("aXaXa", Build("a"), before: 0);

        Assert.Equal(4, hit!.Value.Offset);
    }

    [Fact]
    public void A_zero_length_pattern_terminates_instead_of_looping_forever()
    {
        // "a*" coincide con la cadena vacía en CADA posición. Sin el salto explícito de
        // Enumerate, este recorrido no avanzaría nunca.
        var matches = TextSearch.FindAll("bab", Build("a*", useRegex: true));

        Assert.Single(matches);
        Assert.Equal(1, matches[0].Offset);
        Assert.Equal(1, matches[0].Length);
    }

    [Fact]
    public void FindAll_returns_hits_in_order_and_without_overlapping()
    {
        var matches = TextSearch.FindAll("aaaa", Build("aa"));

        Assert.Equal(2, matches.Count);
        Assert.Equal(0, matches[0].Offset);
        Assert.Equal(2, matches[1].Offset);
    }

    // ─── Reemplazo ───────────────────────────────────────────────────────────

    [Fact]
    public void ReplacementAt_returns_null_when_the_selection_is_not_a_hit()
    {
        var regex = Build("vault");

        // Los tres primeros caracteres de "vault" NO son una coincidencia completa.
        Assert.Null(TextSearch.ReplacementAt("vault", regex, 0, 3, "x", useRegex: false));
        Assert.Equal("x", TextSearch.ReplacementAt("vault", regex, 0, 5, "x", useRegex: false));
    }

    [Fact]
    public void ReplaceAll_keeps_a_dollar_sign_literal_when_regex_is_off()
    {
        var result = TextSearch.ReplaceAll(
            "el precio final", Build("precio"), "US$1", useRegex: false, out var count);

        Assert.Equal(1, count);
        Assert.Equal("el US$1 final", result);
    }

    [Fact]
    public void ReplaceAll_expands_captured_groups_when_regex_is_on()
    {
        var result = TextSearch.ReplaceAll(
            "user@host", Build(@"(\w+)@(\w+)", useRegex: true), "$2:$1",
            useRegex: true, out var count);

        Assert.Equal(1, count);
        Assert.Equal("host:user", result);
    }

    [Fact]
    public void ReplaceAll_reports_zero_and_leaves_the_text_alone_when_nothing_matches()
    {
        var result = TextSearch.ReplaceAll("hola", Build("chau"), "x", useRegex: false, out var count);

        Assert.Equal(0, count);
        Assert.Equal("hola", result);
    }

    [Fact]
    public void BuildReplaceAll_uses_offsets_of_the_untouched_text()
    {
        // El aplicador va de la última a la primera justamente porque estos offsets son
        // del texto original: si se aplicaran de adelante hacia atrás, un reemplazo más
        // largo o más corto correría todos los que faltan.
        var edits = TextSearch.BuildReplaceAll("aa aa", Build("aa"), "bbb", useRegex: false);

        Assert.Equal(2, edits.Count);
        Assert.Equal(0, edits[0].Offset);
        Assert.Equal(3, edits[1].Offset);
        Assert.All(edits, e => Assert.Equal(2, e.Length));
        Assert.All(edits, e => Assert.Equal("bbb", e.Text));
    }

    [Fact]
    public void A_replacement_that_contains_the_pattern_does_not_match_itself_again()
    {
        // "log" -> "catalogo": si el recorrido reusara el texto ya reemplazado, esto no
        // terminaría. ReplaceAll trabaja siempre contra el original, así que da 1.
        var result = TextSearch.ReplaceAll("log", Build("log"), "catalogo", useRegex: false, out var count);

        Assert.Equal(1, count);
        Assert.Equal("catalogo", result);
    }
}
