using System.Collections.Generic;

namespace MarkdownVault.Helpers;

/// <summary>
/// Horizontal placement of one rendered tab inside the strip: <paramref name="Left"/> is its
/// left edge and <paramref name="Width"/> its rendered width, both in the coordinate space of
/// the tab strip's items host (so scrolling is already accounted for by the caller).
/// </summary>
public readonly record struct TabSpan(double Left, double Width);

/// <summary>
/// Decides where a tab being dragged should land — the pure logic behind drag-to-reorder in
/// <c>TabStripView</c>. Kept out of the code-behind for the same reason as
/// <see cref="SpellCheckWordResolver"/>: the geometry rule is the only part with real
/// behaviour, and it is worth testing without spinning up a WPF visual tree.
/// </summary>
/// <remarks>
/// <para>
/// The dragged tab is modelled as a floating rectangle that follows the pointer — its left
/// edge is <c>cursorX - grabOffset</c>, so it keeps the grip the user took on it — and the
/// remaining tabs are re-packed as they would sit with it removed. The drop slot is simply how
/// many of those re-packed neighbours have their centre to the left of the floating tab's LEFT
/// EDGE. Comparing against the left edge rather than the floating centre is what makes the
/// rule agree with reality when nothing has moved yet: a tab sitting at rest resolves to the
/// slot it is already in, for every width combination.
/// </para>
/// <para>
/// The point of that detour is that the answer depends ONLY on the cursor, never on the slot
/// the tab currently occupies — the neighbours pack to the same positions whether the dragged
/// tab is picked out from the left end or the right. That makes the result idempotent, which
/// is what kills flicker. The obvious alternative (compare the cursor against the centres of
/// the tabs as currently laid out) fails exactly here: tab width tracks the file name between
/// MinWidth 80 and MaxWidth 200, and for a narrow tab dragged past a wide one there is a band
/// of cursor positions where the rule says "move right", and then — from the layout that move
/// produced, with the pointer perfectly still — says "move back left", ping-ponging the tab
/// for as long as the user holds it there.
/// </para>
/// </remarks>
public static class TabReorderCalculator
{
    /// <summary>
    /// Returns the index the tab at <paramref name="currentIndex"/> should occupy while the
    /// pointer is at <paramref name="cursorX"/>, or <paramref name="currentIndex"/> itself when
    /// nothing should move. <paramref name="spans"/> must be the placements of every tab in
    /// visual order, the dragged one included; <paramref name="grabOffset"/> is how far from
    /// that tab's left edge the drag was started, captured once at press time and kept for the
    /// whole gesture — a tab grabbed by its right edge therefore has to travel its whole width
    /// before it displaces anything, instead of jumping the moment the pointer moves. Cursors
    /// past either end fall out as the first and last slot, so dragging beyond the strip parks
    /// the tab at the edge rather than doing nothing.
    /// </summary>
    public static int TargetIndex(
        IReadOnlyList<TabSpan> spans, int currentIndex, double cursorX, double grabOffset)
    {
        if (spans is null || spans.Count == 0) return currentIndex;
        if (currentIndex < 0 || currentIndex >= spans.Count) return currentIndex;
        if (spans.Count == 1) return currentIndex;

        // Leading edge of the dragged tab as the pointer currently holds it.
        double draggedLeft = cursorX - grabOffset;

        // Walk the other tabs re-packed from the strip's origin, counting the ones the dragged
        // tab has got in front of. Centres grow monotonically, so the count IS the slot.
        double left = spans[0].Left;
        int slot = 0;

        for (int i = 0; i < spans.Count; i++)
        {
            if (i == currentIndex) continue;

            if (left + spans[i].Width / 2 < draggedLeft) slot++;
            left += spans[i].Width;
        }

        return slot;
    }
}
