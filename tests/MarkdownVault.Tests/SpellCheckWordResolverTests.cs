using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MarkdownVault.Helpers;
using MarkdownVault.Services;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Tests the pure "which misspelled word is under this column" logic that backs the
/// right-click suggestions menu. It must stay consistent with the underline colorizer:
/// same <see cref="MarkdownProseMask"/> masking, same <see cref="ISpellCheckService.Check"/>
/// spans — so the menu only ever offers to fix a word that is actually underlined.
/// </summary>
public class SpellCheckWordResolverTests
{
    /// <summary>
    /// Minimal spell-check double: flags configured words (case-insensitive) as misspelled
    /// by scanning letter runs, and returns canned suggestions. Offsets returned by
    /// <see cref="Check"/> are relative to the text it was handed — exactly like the real COM service.
    /// </summary>
    private sealed class FakeSpell : ISpellCheckService
    {
        private readonly HashSet<string> _bad;
        private readonly Dictionary<string, IReadOnlyList<string>> _suggest;

        public FakeSpell(
            IEnumerable<string> bad,
            Dictionary<string, IReadOnlyList<string>>? suggest = null)
        {
            _bad     = new HashSet<string>(bad, StringComparer.OrdinalIgnoreCase);
            _suggest = suggest ?? new Dictionary<string, IReadOnlyList<string>>();
        }

        public bool   IsAvailable => true;
        public string LanguageTag => "es-ES";

        public IReadOnlyList<SpellError> Check(string text)
        {
            var list = new List<SpellError>();
            foreach (Match m in Regex.Matches(text, @"\p{L}+"))
                if (_bad.Contains(m.Value))
                    list.Add(new SpellError(m.Index, m.Length));
            return list;
        }

        public IReadOnlyList<string> Suggest(string word) =>
            _suggest.TryGetValue(word, out var s) ? s : Array.Empty<string>();
    }

    [Fact]
    public void Returns_word_when_column_inside_a_misspelled_span()
    {
        var spell = new FakeSpell(new[] { "helo" });
        //            0123456789
        var result = SpellCheckWordResolver.FindMisspelledWordAt(spell, "say helo now", column: 5);

        Assert.NotNull(result);
        Assert.Equal("helo", result!.Value.Word);
        Assert.Equal(4, result.Value.Offset);
        Assert.Equal(4, result.Value.Length);
    }

    [Fact]
    public void Returns_null_when_column_is_on_a_correctly_spelled_word()
    {
        var spell  = new FakeSpell(new[] { "helo" });
        var result = SpellCheckWordResolver.FindMisspelledWordAt(spell, "say helo now", column: 1);

        Assert.Null(result);
    }

    [Fact]
    public void Matches_at_the_first_and_last_character_of_the_word()
    {
        var spell = new FakeSpell(new[] { "helo" });

        Assert.NotNull(SpellCheckWordResolver.FindMisspelledWordAt(spell, "say helo now", column: 4)); // 'h'
        Assert.NotNull(SpellCheckWordResolver.FindMisspelledWordAt(spell, "say helo now", column: 7)); // 'o'
    }

    [Fact]
    public void Ignores_misspellings_inside_masked_regions_like_inline_code()
    {
        // `helo` is inside inline code → MarkdownProseMask blanks it → never a target.
        var spell  = new FakeSpell(new[] { "helo" });
        var result = SpellCheckWordResolver.FindMisspelledWordAt(spell, "run `helo` please", column: 6);

        Assert.Null(result);
    }

    [Fact]
    public void Word_span_maps_back_onto_the_original_line_after_masking()
    {
        // A masked URL precedes the misspelled word; the returned offset/length must index
        // the ORIGINAL line so the caller replaces the right characters.
        var spell = new FakeSpell(new[] { "wrold" });
        var line  = "see [x](http://a.com) wrold end";
        var col   = line.IndexOf("wrold", StringComparison.Ordinal) + 1;

        var result = SpellCheckWordResolver.FindMisspelledWordAt(spell, line, col);

        Assert.NotNull(result);
        Assert.Equal("wrold", line.Substring(result!.Value.Offset, result.Value.Length));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void Returns_null_for_out_of_range_columns(int column)
    {
        var spell = new FakeSpell(new[] { "helo" });
        Assert.Null(SpellCheckWordResolver.FindMisspelledWordAt(spell, "say helo now", column));
    }
}
