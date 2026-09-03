using System.Collections.Generic;
using MarkdownVault.Helpers;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Tests the pure "which slot does the dragged tab land in" logic behind drag-to-reorder in
/// the tab strip. Two properties matter far more than the exact thresholds: a tab nobody has
/// moved yet must resolve to the slot it already occupies, and re-running the rule on the
/// layout a move produced — with the pointer perfectly still — must produce that same move
/// again. Tabs are sized to their file name (MinWidth 80 / MaxWidth 200), and a rule that
/// lacks either property makes an unequal pair ping-pong between two slots.
/// </summary>
public class TabReorderCalculatorTests
{
    /// <summary>Tabs laid out left to right from x = 0, packed edge to edge.</summary>
    private static List<TabSpan> Strip(params double[] widths)
    {
        var spans = new List<TabSpan>(widths.Length);
        double x = 0;
        foreach (var w in widths)
        {
            spans.Add(new TabSpan(x, w));
            x += w;
        }
        return spans;
    }

    /// <summary>
    /// Nothing has been dragged anywhere: the pointer is still exactly where it pressed, so
    /// every tab must resolve to its own slot. Widths are deliberately all different — this is
    /// the case a centre-based comparison gets wrong for the wide tab.
    /// </summary>
    [Theory]
    [InlineData(0,  10)]
    [InlineData(1,  40)]
    [InlineData(2, 150)]
    public void AtRest_EveryTabResolvesToItsOwnSlot(int index, double grabOffset)
    {
        var spans = Strip(200, 80, 160);
        double cursorX = spans[index].Left + grabOffset;

        Assert.Equal(index, TabReorderCalculator.TargetIndex(spans, index, cursorX, grabOffset));
    }

    [Fact]
    public void MovesRight_OnceTheDraggedEdgePassesTheNeighboursCentre()
    {
        //  A[0..100) B[100..200), grabbed at A's centre. With A lifted out, B packs to
        //  [0..100) — centre 50 — so A displaces it once A's left edge is past x = 50,
        //  i.e. once the pointer is past x = 100.
        var spans = Strip(100, 100);

        Assert.Equal(0, TabReorderCalculator.TargetIndex(spans, 0, cursorX:  99, grabOffset: 50));
        Assert.Equal(1, TabReorderCalculator.TargetIndex(spans, 0, cursorX: 101, grabOffset: 50));
    }

    [Fact]
    public void MovesLeft_OnceTheDraggedEdgePassesTheNeighboursCentre()
    {
        //  Mirror of the above: B at index 1 grabbed at its centre (pointer starts at x = 150).
        var spans = Strip(100, 100);

        Assert.Equal(1, TabReorderCalculator.TargetIndex(spans, 1, cursorX: 101, grabOffset: 50));
        Assert.Equal(0, TabReorderCalculator.TargetIndex(spans, 1, cursorX:  99, grabOffset: 50));
    }

    [Fact]
    public void ClampsToTheEnds_WhenDraggedBeyondTheStrip()
    {
        var spans = Strip(100, 100, 100);

        Assert.Equal(0, TabReorderCalculator.TargetIndex(spans, 2, cursorX: -500, grabOffset: 50));
        Assert.Equal(2, TabReorderCalculator.TargetIndex(spans, 0, cursorX: 5000, grabOffset: 50));
    }

    /// <summary>
    /// The grip is part of the gesture: a wide tab taken by its right edge must travel before
    /// it shoves anything aside. Grabbing A[0..200) at x = 190 and nudging the pointer to 210 —
    /// past the neighbour's left edge already — must NOT reorder, because A's own left edge has
    /// barely left the origin. A rule that treats the pointer as the tab's centre swaps here.
    /// </summary>
    [Fact]
    public void RespectsTheGrip_WhenAWideTabIsTakenByItsEdge()
    {
        var spans = Strip(200, 100);

        Assert.Equal(0, TabReorderCalculator.TargetIndex(spans, 0, cursorX: 210, grabOffset: 190));
        Assert.Equal(1, TabReorderCalculator.TargetIndex(spans, 0, cursorX: 260, grabOffset: 190));
    }

    /// <summary>
    /// The anti-flicker guarantee, on the pair that breaks the naive rule: a narrow tab dragged
    /// past a wide one. Re-running the rule on the layout the move produced, pointer unmoved,
    /// must report the same slot — otherwise the tab oscillates for as long as the user holds it.
    /// </summary>
    [Fact]
    public void IsIdempotent_AfterAMoveBetweenUnequalWidths()
    {
        //  before: A[0..60) B[60..160)  — A grabbed at its centre, pointer dragged to x = 100
        var before = Strip(60, 100);
        Assert.Equal(1, TabReorderCalculator.TargetIndex(before, 0, cursorX: 100, grabOffset: 30));

        //  after:  B[0..100) A[100..160)  — same pointer, A now at index 1
        var after = Strip(100, 60);
        Assert.Equal(1, TabReorderCalculator.TargetIndex(after, 1, cursorX: 100, grabOffset: 30));
    }

    /// <summary>Same guarantee with the widths swapped — wide tab dragged past a narrow one.</summary>
    [Fact]
    public void IsIdempotent_WhenTheDraggedTabIsTheWideOne()
    {
        //  before: A[0..100) B[100..160) — A grabbed at its centre, pointer dragged to x = 90
        var before = Strip(100, 60);
        Assert.Equal(1, TabReorderCalculator.TargetIndex(before, 0, cursorX: 90, grabOffset: 50));

        //  after:  B[0..60) A[60..160)
        var after = Strip(60, 100);
        Assert.Equal(1, TabReorderCalculator.TargetIndex(after, 1, cursorX: 90, grabOffset: 50));
    }

    /// <summary>
    /// Dragging across two neighbours at once (a fast flick) lands on the far slot rather than
    /// crawling one position per mouse move.
    /// </summary>
    [Fact]
    public void SkipsSeveralSlots_OnAFastDrag()
    {
        var spans = Strip(100, 100, 100, 100);

        Assert.Equal(3, TabReorderCalculator.TargetIndex(spans, 0, cursorX: 350, grabOffset: 50));
    }

    [Fact]
    public void ReturnsCurrentIndex_WhenThereIsNothingToMeasure()
    {
        Assert.Equal(3, TabReorderCalculator.TargetIndex(new List<TabSpan>(), 3, 42, 10));
        Assert.Equal(0, TabReorderCalculator.TargetIndex(Strip(100), 0, 5000, 10));   // lone tab
        Assert.Equal(-1, TabReorderCalculator.TargetIndex(Strip(100, 100), -1, 42, 10));
        Assert.Equal(7, TabReorderCalculator.TargetIndex(Strip(100, 100), 7, 42, 10));
    }
}
