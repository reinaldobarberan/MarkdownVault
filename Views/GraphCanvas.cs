using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MarkdownVault.Models;
using MarkdownVault.ViewModels;

namespace MarkdownVault.Views;

/// <summary>
/// Immediate-mode rendering surface for the vault graph — the WPF analogue of the
/// prototype's HTML canvas. Runs the force simulation on <c>CompositionTarget.Rendering</c>
/// (~60 fps) and paints nodes/links/labels each frame via <see cref="OnRender"/>.
/// Camera (pan/zoom) and node drag/hover are handled here in device coordinates.
/// </summary>
public sealed class GraphCanvas : FrameworkElement
{
    // ── Camera ──
    private double _scale = 0.85;
    private double _ox    = 90;
    private double _oy    = 0;

    // ── Interaction ──
    private GraphNode? _hover;
    private GraphNode? _dragNode;
    private bool       _panning;
    private Point      _lastPointer;
    private double     _moved;

    // ── Frozen brushes (colours from the design handoff) ──
    private static readonly Brush NodeNormal = Frozen("#6F9FD8");
    private static readonly Brush NodeActive = Frozen("#E0AF68");
    private static readonly Brush NodeHover  = Frozen("#9ECBFF");
    private static readonly Brush ActiveRing = Frozen("#FFFFFF");
    private static readonly Color LinkColor  = Color.FromRgb(90, 90, 90);
    private static readonly Color LinkHot    = Color.FromRgb(111, 168, 220);

    private GraphViewModel? Vm => DataContext as GraphViewModel;

    public GraphCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Loaded   += (_, _) => CompositionTarget.Rendering += OnFrame;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnFrame;
        DataContextChanged += (_, _) =>
        {
            if (Vm is not null) Vm.GraphRebuilt += OnGraphRebuilt;
        };
    }

    // ── Public camera controls (wired to the zoom buttons) ──

    public void ZoomIn()  => _scale = Math.Min(4,    _scale * 1.2);
    public void ZoomOut() => _scale = Math.Max(0.25, _scale / 1.2);

    /// <summary>Centre button: reset zoom to 1× and recentre.</summary>
    public void ResetView()
    {
        _scale = 1;
        _ox = 0;
        _oy = 0;
    }

    /// <summary>Recommended framing after a (re)build — clears offset from the filter panel.</summary>
    private void OnGraphRebuilt()
    {
        _scale = 0.85;
        _ox = 90;
        _oy = 0;
        _hover = null;
    }

    // ── Frame loop ──

    private void OnFrame(object? sender, EventArgs e)
    {
        // Skip work while the graph is collapsed (editor mode is active).
        if (!IsVisible || Vm is null) return;
        Tick();
        InvalidateVisual();
    }

    /// <summary>One step of the force simulation — mirrors the prototype's <c>_tick</c>.</summary>
    private void Tick()
    {
        var vm = Vm!;
        var visible = new List<GraphNode>();
        foreach (var n in vm.Nodes) if (n.Visible) visible.Add(n);

        double repel   = 14000 * vm.ForceRepel;
        double linkK   = 0.02 * vm.ForceLink;
        double centerK = 0.006 * vm.ForceCenter;
        const double linkLen = 130;

        // Repulsion between every visible pair.
        for (int i = 0; i < visible.Count; i++)
        {
            var a = visible[i];
            for (int j = i + 1; j < visible.Count; j++)
            {
                var b = visible[j];
                double dx = a.X - b.X, dy = a.Y - b.Y;
                double d2 = dx * dx + dy * dy;
                if (d2 < 0.01) d2 = 0.01;
                double d = Math.Sqrt(d2);
                double f = repel / d2;
                double ux = dx / d, uy = dy / d;
                a.Vx += ux * f * 0.0016; a.Vy += uy * f * 0.0016;
                b.Vx -= ux * f * 0.0016; b.Vy -= uy * f * 0.0016;
            }
        }

        // Spring force along links.
        foreach (var l in vm.Links)
        {
            var a = l.Source; var b = l.Target;
            if (!a.Visible || !b.Visible) continue;
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 0.01) d = 0.01;
            double f = (d - linkLen) * linkK;
            double ux = dx / d, uy = dy / d;
            a.Vx += ux * f; a.Vy += uy * f;
            b.Vx -= ux * f; b.Vy -= uy * f;
        }

        // Gravity toward centre + integration.
        foreach (var n in visible)
        {
            n.Vx += -n.X * centerK;
            n.Vy += -n.Y * centerK;
            if (n.Fx is not null)
            {
                n.X = n.Fx.Value; n.Y = n.Fy!.Value;
                n.Vx = n.Vy = 0;
                continue;
            }
            n.Vx *= 0.82; n.Vy *= 0.82;
            n.X += n.Vx; n.Y += n.Vy;
        }
    }

    // ── Rendering ──

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;

        var bg = (TryFindResource("GraphBackground") as Brush) ?? Brushes.Black;
        dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));

        var vm = Vm;
        if (vm is null || vm.Nodes.Count == 0) return;

        var labelBrush = (TryFindResource("GraphLabelBrush") as Brush) ?? Brushes.Gray;

        // Camera: world → screen = world * scale + (centre + offset).
        var matrix = new Matrix(_scale, 0, 0, _scale, w / 2 + _ox, h / 2 + _oy);
        dc.PushTransform(new MatrixTransform(matrix));

        var active   = vm.ActiveFile;
        var focus    = _hover;
        var focusId  = focus?.Id;
        var nbrs     = focus is null ? null : Neighbours(focus.Id);

        // Pens are scaled to keep a constant on-screen thickness.
        var penNormal = FrozenPen(LinkColor, 0.55, 1.0 / _scale);
        var penDim    = FrozenPen(LinkColor, 0.25, 1.0 / _scale);
        var penHot    = FrozenPen(LinkHot,   0.85, 1.6 / _scale);
        var ringPen   = FrozenPen(Colors.White, 1.0, 2.0 / _scale);

        // ── Links ──
        foreach (var l in vm.Links)
        {
            var a = l.Source; var b = l.Target;
            if (!a.Visible || !b.Visible) continue;
            bool hot = focusId is not null && (a.Id == focusId || b.Id == focusId);
            var pen = hot ? penHot : (focusId is not null ? penDim : penNormal);
            dc.DrawLine(pen, new Point(a.X, a.Y), new Point(b.X, b.Y));
        }

        // ── Nodes + labels ──
        bool showLabels = vm.ShowLabels;
        foreach (var n in vm.Nodes)
        {
            if (!n.Visible) continue;
            double r = Radius(n);
            bool isActive = n.Id == active;
            bool dim = focusId is not null && n.Id != focusId && !(nbrs is not null && nbrs.Contains(n.Id));

            Brush fill = isActive ? NodeActive : NodeNormal;
            if (n.Id == focusId) fill = NodeHover;

            if (dim) dc.PushOpacity(0.28);
            dc.DrawEllipse(fill, isActive ? ringPen : null, new Point(n.X, n.Y), r, r);
            if (dim) dc.Pop();

            if (showLabels && (_scale > 0.55 || n.Id == focusId || isActive))
            {
                double em = 11.0 / _scale;
                var ft = new FormattedText(
                    n.Label,
                    System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    _uiTypeface,
                    em,
                    labelBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                double ox = n.X - ft.Width / 2;
                double oy = n.Y + r + 12.0 / _scale;
                dc.PushOpacity(dim ? 0.3 : 0.92);
                dc.DrawText(ft, new Point(ox, oy));
                dc.Pop();
            }
        }

        dc.Pop();
    }

    private static readonly Typeface _uiTypeface =
        new("Segoe UI");

    // ── Model helpers ──

    private static double Radius(GraphNode n) => 4 + Math.Sqrt(n.Degree) * 3.2;

    private HashSet<string> Neighbours(string id)
    {
        var set = new HashSet<string>();
        foreach (var l in Vm!.Links)
        {
            if (l.Source.Id == id) set.Add(l.Target.Id);
            if (l.Target.Id == id) set.Add(l.Source.Id);
        }
        return set;
    }

    private Point ToWorld(Point p) => new(
        (p.X - ActualWidth  / 2 - _ox) / _scale,
        (p.Y - ActualHeight / 2 - _oy) / _scale);

    private GraphNode? Pick(Point world)
    {
        if (Vm is null) return null;
        GraphNode? best = null;
        double bestDist = double.MaxValue;
        foreach (var n in Vm.Nodes)
        {
            if (!n.Visible) continue;
            double d = Math.Sqrt((n.X - world.X) * (n.X - world.X) + (n.Y - world.Y) * (n.Y - world.Y));
            double r = Radius(n) + 6 / _scale;
            if (d < r && d < bestDist) { bestDist = d; best = n; }
        }
        return best;
    }

    // ── Input ──

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var world = ToWorld(e.GetPosition(this));
        _lastPointer = e.GetPosition(this);
        _moved = 0;

        var node = Pick(world);
        if (node is not null)
        {
            _dragNode = node;
            node.Fx = node.X; node.Fy = node.Y;
            Cursor = Cursors.SizeAll;
        }
        else
        {
            _panning = true;
            Cursor = Cursors.SizeAll;
        }
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);

        if (_dragNode is not null)
        {
            var world = ToWorld(p);
            _dragNode.Fx = world.X; _dragNode.Fy = world.Y;
            _dragNode.X = world.X;  _dragNode.Y = world.Y;
            _moved += Math.Abs(p.X - _lastPointer.X) + Math.Abs(p.Y - _lastPointer.Y);
        }
        else if (_panning)
        {
            _ox += p.X - _lastPointer.X;
            _oy += p.Y - _lastPointer.Y;
            _moved += Math.Abs(p.X - _lastPointer.X) + Math.Abs(p.Y - _lastPointer.Y);
        }
        else
        {
            _hover = Pick(ToWorld(p));
            Cursor = _hover is not null ? Cursors.Hand : Cursors.Arrow;
        }

        _lastPointer = p;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragNode is not null && _moved < 4)
            Vm?.RequestOpen(_dragNode.Id);

        if (_dragNode is not null)
        {
            _dragNode.Fx = _dragNode.Fy = null;
        }
        _dragNode = null;
        _panning  = false;
        Cursor = Cursors.Arrow;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        double f = e.Delta > 0 ? 1.12 : 1 / 1.12;
        _scale = Math.Max(0.25, Math.Min(4, _scale * f));
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_dragNode is null && !_panning) _hover = null;
    }

    // ── Brush/pen factories ──

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static Pen FrozenPen(Color color, double alpha, double thickness)
    {
        var c = Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B);
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
