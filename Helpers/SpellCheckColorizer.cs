using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using MarkdownVault.Services;

namespace MarkdownVault.Helpers;

/// <summary>
/// Underlines misspelled words in the AvalonEdit editor with a red wavy line.
/// Implemented as a <see cref="DocumentColorizingTransformer"/> — it runs during
/// visual-line construction, so it re-applies automatically on every render
/// (typing, scrolling, Redraw). This avoids the manual-repaint problem that an
/// <c>IBackgroundRenderer</c> hits in this AvalonEdit fork.
/// </summary>
public sealed class SpellCheckColorizer : DocumentColorizingTransformer
{
    private static readonly TextDecorationCollection Squiggle = CreateSquiggle();

    private readonly ISpellCheckService _spell;
    private readonly Dictionary<string, IReadOnlyList<SpellError>> _cache = new();

    // Skip-set (fenced code + YAML frontmatter), recomputed only when the text length changes.
    private readonly HashSet<int> _skipLines = new();
    private int _skipCacheLength = -1;

    /// <summary>When false the colorizer is a no-op (e.g. for HTML/Mermaid files).</summary>
    public bool Enabled { get; set; } = true;

    public SpellCheckColorizer(ISpellCheckService spell) => _spell = spell;

    protected override void ColorizeLine(DocumentLine line)
    {
        if (!Enabled || !_spell.IsAvailable || line.Length == 0)
            return;

        var doc = CurrentContext.Document;
        EnsureSkipSet(doc);
        if (_skipLines.Contains(line.LineNumber))
            return;

        string text   = doc.GetText(line.Offset, line.Length);
        string masked = MarkdownProseMask.Mask(text);
        if (string.IsNullOrWhiteSpace(masked))
            return;

        var spans = CheckCached(masked);
        if (spans.Count == 0) return;

        foreach (var span in spans)
        {
            int segStart = line.Offset + span.Offset;
            int segEnd   = segStart + span.Length;
            if (segEnd <= segStart || segEnd > line.EndOffset) continue;

            ChangeLinePart(segStart, segEnd,
                el => el.TextRunProperties.SetTextDecorations(Squiggle));
        }
    }

    // ─── Spell-check caching ──────────────────────────────────────────────────

    private IReadOnlyList<SpellError> CheckCached(string maskedLine)
    {
        if (_cache.TryGetValue(maskedLine, out var cached))
            return cached;

        var errors = _spell.Check(maskedLine);
        if (_cache.Count > 4000) _cache.Clear();
        _cache[maskedLine] = errors;
        return errors;
    }

    // ─── Fenced code + frontmatter skip-set ───────────────────────────────────

    private void EnsureSkipSet(TextDocument doc)
    {
        if (doc.TextLength == _skipCacheLength) return;
        _skipCacheLength = doc.TextLength;
        _skipLines.Clear();

        bool inFence = false, inFront = false;
        int index = 0;

        foreach (var l in doc.Lines)
        {
            string t = doc.GetText(l.Offset, l.Length).Trim();

            if (index == 0 && t == "---") { inFront = true; _skipLines.Add(l.LineNumber); index++; continue; }
            if (inFront)
            {
                _skipLines.Add(l.LineNumber);
                if (t is "---" or "...") inFront = false;
                index++;
                continue;
            }

            if (t.StartsWith("```") || t.StartsWith("~~~"))
            {
                inFence = !inFence;
                _skipLines.Add(l.LineNumber);
                index++;
                continue;
            }
            if (inFence) { _skipLines.Add(l.LineNumber); index++; continue; }

            index++;
        }
    }

    // ─── Wavy red underline decoration ────────────────────────────────────────

    private static TextDecorationCollection CreateSquiggle()
    {
        // One triangular wave tile, repeated horizontally to form the squiggle.
        var wave = new StreamGeometry();
        using (var ctx = wave.Open())
        {
            ctx.BeginFigure(new Point(0, 2.0), false, false);
            ctx.LineTo(new Point(2, 0.0), true, false);
            ctx.LineTo(new Point(4, 2.0), true, false);
        }
        wave.Freeze();

        var strokePen = new Pen(new SolidColorBrush(Color.FromRgb(0xE5, 0x1C, 0x23)), 1.1);
        strokePen.Freeze();

        var drawing = new GeometryDrawing { Geometry = wave, Pen = strokePen };
        drawing.Freeze();

        var brush = new DrawingBrush(drawing)
        {
            TileMode      = TileMode.Tile,
            Viewport      = new Rect(0, 0, 4, 3),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch       = Stretch.None,
        };
        brush.Freeze();

        var wavyPen = new Pen(brush, 3);
        wavyPen.Freeze();

        var decoration = new TextDecoration
        {
            Location         = TextDecorationLocation.Underline,
            Pen              = wavyPen,
            PenThicknessUnit = TextDecorationUnit.Pixel,
        };
        decoration.Freeze();

        var collection = new TextDecorationCollection { decoration };
        collection.Freeze();
        return collection;
    }
}
