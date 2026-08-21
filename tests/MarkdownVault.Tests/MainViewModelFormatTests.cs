using MarkdownVault.ViewModels;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Cubre <see cref="MainViewModel.FormatPercent"/> y <see cref="MainViewModel.FormatOthers"/>:
/// el formateo de la barra de progreso del SDK 1.3.0 (ver <c>MainViewModel.ShowProgress</c>).
/// Ambos son <c>internal static</c>, visibles acá vía <c>InternalsVisibleTo</c>
/// (MarkdownVault.csproj). Casos portados desde el port verificado en el scratchpad
/// (<c>verify_progress.py</c>, <c>format_percent</c> / <c>format_others</c>) contra la
/// lógica REAL.
/// </summary>
public class MainViewModelFormatTests
{
    // ── FormatPercent ────────────────────────────────────────────────────────

    [Fact]
    public void FormatPercent_of_null_is_the_empty_string()
    {
        // percent == null ⇒ modo indeterminado: no hay número que mostrar.
        Assert.Equal("", MainViewModel.FormatPercent(null));
    }

    [Theory]
    [InlineData(0.0, "0 %")]
    [InlineData(57.3, "57 %")]
    [InlineData(57.8, "58 %")]
    [InlineData(100.0, "100 %")]
    public void FormatPercent_rounds_to_whole_numbers_with_a_percent_sign(double percent, string expected)
    {
        Assert.Equal(expected, MainViewModel.FormatPercent(percent));
    }

    // ── FormatOthers ─────────────────────────────────────────────────────────

    [Fact]
    public void FormatOthers_of_zero_or_negative_is_the_empty_string()
    {
        Assert.Equal("", MainViewModel.FormatOthers(0));
        Assert.Equal("", MainViewModel.FormatOthers(-1));
    }

    [Theory]
    [InlineData(1, "+1 en segundo plano")]
    [InlineData(3, "+3 en segundo plano")]
    public void FormatOthers_counts_the_scopes_left_behind(int others, string expected)
    {
        Assert.Equal(expected, MainViewModel.FormatOthers(others));
    }
}
