namespace MarkdownVault.Helpers;

/// <summary>
/// Generates the <c>^id</c> shape MarkdownVault writes for a new block marker: Obsidian's own
/// convention, 6 lowercase base-36 characters (proposal decision #1). Kept pure — no file I/O,
/// no editor types — so collision handling is unit-testable with a seeded <see cref="Random"/>
/// (Phase 8) without constructing a document.
/// </summary>
public static class MarkerId
{
    private const string Base36Chars = "0123456789abcdefghijklmnopqrstuvwxyz";
    private static readonly Random SharedRandom = new();

    /// <summary>
    /// Generates a fresh id, retrying up to 5 times against <paramref name="existingIds"/>
    /// (the buffer's own <c>GetBlockMarkers</c> result) before widening to 8 characters rather
    /// than looping forever — at 8 base-36 characters a collision is astronomically unlikely,
    /// so this always terminates.
    /// </summary>
    /// <param name="existingIds">Ids already used in the current buffer (bare, no <c>^</c>).</param>
    /// <param name="random">
    /// Injectable for deterministic tests (Phase 8); defaults to a shared instance in production.
    /// </param>
    public static string Generate(IReadOnlyCollection<string> existingIds, Random? random = null)
    {
        random ??= SharedRandom;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = GenerateRaw(random, 6);
            if (!existingIds.Contains(candidate))
                return candidate;
        }
        return GenerateRaw(random, 8);
    }

    private static string GenerateRaw(Random random, int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Base36Chars[random.Next(Base36Chars.Length)];
        return new string(chars);
    }
}
