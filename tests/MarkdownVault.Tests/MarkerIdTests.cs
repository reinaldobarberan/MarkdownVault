using MarkdownVault.Helpers;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers <see cref="MarkerId.Generate"/>: shape (6 lowercase base-36 chars by default) and
/// collision-driven regeneration (5 retries at 6 chars, then widening to 8) — design decision
/// #8. Deterministic via the injectable <see cref="Random"/> parameter (added specifically for
/// this, per apply-progress.md batch 1), so no test is flaky on the shared production instance.
/// </summary>
public class MarkerIdTests
{
    private const string Base36Chars = "0123456789abcdefghijklmnopqrstuvwxyz";

    [Fact]
    public void Generate_with_no_collisions_returns_six_lowercase_base36_chars()
    {
        var id = MarkerId.Generate(existingIds: Array.Empty<string>(), random: new Random(1));

        Assert.Equal(6, id.Length);
        Assert.All(id, c => Assert.Contains(c, Base36Chars));
    }

    [Fact]
    public void Generate_is_deterministic_for_a_given_seed()
    {
        var idA = MarkerId.Generate(Array.Empty<string>(), new Random(7));
        var idB = MarkerId.Generate(Array.Empty<string>(), new Random(7));

        Assert.Equal(idA, idB);
    }

    [Fact]
    public void Generate_retries_up_to_five_times_then_widens_to_eight_chars_on_collision()
    {
        // Every one of the first 5 attempts (5 * 6 = 30 calls to Next) is forced to reproduce
        // "aaaaaa" — already in existingIds — so all 5 six-char attempts collide. The 6th
        // generation (8 chars) then gets a fresh, non-colliding sequence.
        var forcedCollisionIndex = Base36Chars.IndexOf('a'); // 10
        var collisionValues = Enumerable.Repeat(forcedCollisionIndex, 30);
        var widenedValues = new[] { 11, 12, 13, 14, 15, 16, 17, 18 }; // "bcdefghi"
        var random = new QueueRandom(collisionValues.Concat(widenedValues));

        var id = MarkerId.Generate(existingIds: new[] { "aaaaaa" }, random: random);

        Assert.Equal(8, id.Length);
        Assert.Equal("bcdefghi", id);
    }

    [Fact]
    public void Generate_accepts_the_first_candidate_that_does_not_collide()
    {
        // First attempt collides ("aaaaaa", 6 calls), second attempt is fresh ("bbbbbb") —
        // Generate must stop retrying as soon as a non-colliding 6-char candidate appears,
        // never falling through to the 8-char widening.
        var collisionAttempt = Enumerable.Repeat(Base36Chars.IndexOf('a'), 6);
        var freshAttempt = Enumerable.Repeat(Base36Chars.IndexOf('b'), 6);
        var random = new QueueRandom(collisionAttempt.Concat(freshAttempt));

        var id = MarkerId.Generate(existingIds: new[] { "aaaaaa" }, random: random);

        Assert.Equal("bbbbbb", id);
    }

    /// <summary>
    /// Deterministic <see cref="Random"/> double: replays a fixed sequence of values from
    /// <see cref="Random.Next(int)"/> instead of sampling, so collision-retry behavior can be
    /// asserted exactly rather than probabilistically.
    /// </summary>
    private sealed class QueueRandom : Random
    {
        private readonly Queue<int> _values;
        public QueueRandom(IEnumerable<int> values) => _values = new Queue<int>(values);

        public override int Next(int maxValue) =>
            _values.Count > 0
                ? _values.Dequeue()
                : throw new InvalidOperationException("QueueRandom ran out of scripted values.");
    }
}
