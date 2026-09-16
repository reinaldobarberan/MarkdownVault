using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MarkdownVault.Models;
using MarkdownVault.Services;
using MarkdownVault.ViewModels;

namespace MarkdownVault.Views;

/// <summary>
/// Immediate-mode rendering surface for the vault graph. Runs the force simulation on
/// <c>CompositionTarget.Rendering</c> (~60 fps) and paints nodes/links/labels each frame via
/// <see cref="OnRender"/>.
///
/// The simulation is always three-dimensional; flat mode is just every node pulled to Z = 0 with
/// the camera square-on, which keeps ONE code path instead of two. Drawing happens in SCREEN
/// coordinates rather than under a world transform, because with perspective every node has its
/// own scale — a single <c>PushTransform</c> cannot express that.
/// </summary>
public sealed class GraphCanvas : FrameworkElement
{
    // ── Camera ──
    private readonly GraphCamera _camera = new();
    private double _ox = 90;          // pan, in screen pixels
    private double _oy;

    /// <summary>
    /// Lowest zoom the camera will go to. Lower than a plain 2D graph would need: the layout
    /// spreads a big vault over a wide area and the orphan halo sits outside all of it.
    /// </summary>
    private const double MinZoom = 0.12;

    /// <summary>Radians of rotation per pixel dragged while orbiting.</summary>
    private const double OrbitSpeed = 0.008;

    /// <summary>
    /// How much colour the furthest node on screen loses to the background. 0 is no depth fading
    /// at all; much above this and the back of the graph disappears instead of receding.
    /// </summary>
    private const double FogStrength = 0.62;

    /// <summary>Distinct fade levels cached. More is invisible to the eye and just more brushes.</summary>
    private const int FogSteps = 24;

    /// <summary>
    /// Pitch limit, a little under a quarter turn. Letting the camera cross the pole flips the
    /// world upside down mid-drag and the user loses all sense of where they were.
    /// </summary>
    private const double MaxPitch = 1.4;

    // ── Interaction ──
    private GraphNode? _hover;
    private GraphNode? _dragNode;
    private bool       _dragWasPinned;
    private double     _dragDepth;    // depth the dragged node keeps while it is moved
    private bool       _panning;
    private bool       _orbiting;
    private Point      _lastPointer;
    private double     _moved;

    // ── Layout tuning ──
    /// <summary>Clear world-space gap kept between two node discs by the collision pass.</summary>
    private const double CollisionPad = 6.0;
    /// <summary>How much of an overlap is undone per frame. Below 1 so nodes settle instead of jittering.</summary>
    private const double CollisionStiffness = 0.5;
    /// <summary>
    /// Barnes-Hut accuracy dial: a cell is treated as one lump when its width over the distance
    /// falls under this. 0 would be exact and O(n²); at 0.7 the error is invisible in a layout
    /// whose whole point is relative placement.
    /// </summary>
    private const double Theta = 0.7;
    /// <summary>
    /// Average kinetic energy under which the layout counts as finished and the simulation stops.
    /// </summary>
    private const double SettleEnergy = 0.0009;
    /// <summary>
    /// Hard ceiling on how long the simulation may run before it is forced to settle — about ten
    /// seconds at sixty frames. Only reached by an over-constrained layout whose overlaps cannot
    /// all be undone; without it such a graph would keep a core busy for as long as it is open.
    /// </summary>
    private const int MaxFramesAwake = 600;

    // ── Simulation scratch (reused every frame, never reallocated) ──
    private readonly GraphOctree _tree = new();
    private readonly List<GraphNode> _simulated = [];
    private readonly GraphCollision _collision = new();
    private bool _settled;
    private bool _needsFit;
    private int  _framesAwake;

    // ── Render scratch ──
    private readonly List<Projected> _projected = [];
    private readonly List<int> _order = [];
    // Depth span of what is currently on screen, refreshed by ProjectVisible. Distance fading is
    // measured against THIS, so the effect is as strong in a shallow graph as in a deep one.
    private double _nearDepth;
    private double _farDepth;
    private readonly Dictionary<long, Brush> _fadedBrushes = new();
    private Color _fogBackground;

    private struct Projected
    {
        public GraphNode Node;
        public double Sx, Sy, Depth, Scale;
    }

    // ── Frozen brushes (colours from the design handoff) ──
    // The plain node colour comes from GraphPalette (see GroupBrush); only the two "always
    // findable" states keep a hard-coded brush here.
    private static readonly Brush NodeActive = Frozen("#E0AF68");
    private static readonly Brush NodeHover  = Frozen("#9ECBFF");
    private static readonly Color LinkColor  = Color.FromRgb(90, 90, 90);
    private static readonly Color LinkHot    = Color.FromRgb(111, 168, 220);
    private static readonly Color PinRing    = Color.FromRgb(183, 196, 209);

    private GraphViewModel? Vm => DataContext as GraphViewModel;

    public GraphCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Loaded   += (_, _) => CompositionTarget.Rendering += OnFrame;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnFrame;
        DataContextChanged += (_, _) => Rebind();
    }

    // The VM the canvas is currently listening to. Kept so a DataContext swap detaches the old
    // handlers instead of stacking a second set on top.
    private GraphViewModel? _boundVm;

    private void Rebind()
    {
        if (_boundVm is not null)
        {
            _boundVm.GraphRebuilt    -= OnGraphRebuilt;
            _boundVm.PropertyChanged -= OnVmPropertyChanged;
        }

        _boundVm = Vm;

        if (_boundVm is not null)
        {
            _boundVm.GraphRebuilt    += OnGraphRebuilt;
            _boundVm.PropertyChanged += OnVmPropertyChanged;
            Wake();
        }
    }

    // Any change on the view model — a filter, a force slider, a folder switched off — can move
    // the layout, so it has to bring a frozen simulation back to life.
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GraphViewModel.ThreeD))
            OnDimensionChanged();

        Wake();
    }

    /// <summary>
    /// Entering 3D tilts the camera off square immediately — leaving it at yaw 0 / pitch 0 would
    /// show a picture identical to the flat one and read as "the switch does nothing". Leaving 3D
    /// snaps it back, because a tilted camera over a flattened layout is just a skewed 2D graph.
    /// </summary>
    private void OnDimensionChanged()
    {
        bool spatial = Vm?.ThreeD == true;
        _camera.Yaw   = spatial ?  0.6 : 0;
        _camera.Pitch = spatial ? 0.35 : 0;
        _needsFit = true;
    }

    // ── Public camera controls (wired to the zoom buttons) ──

    public void ZoomIn()  => _camera.Zoom = Math.Min(4,       _camera.Zoom * 1.2);
    public void ZoomOut() => _camera.Zoom = Math.Max(MinZoom, _camera.Zoom / 1.2);

    /// <summary>Releases every hand-placed node so the simulation lays them out again.</summary>
    public void UnpinAll()
    {
        if (Vm is null) return;
        foreach (var n in Vm.Nodes) n.Unpin();
        Wake();
    }

    /// <summary>Centre button: frame everything that is currently visible, square-on in flat mode.</summary>
    public void ResetView() => FitToContent();

    /// <summary>
    /// Zooms and pans so every visible node fits on screen. It iterates because the answer feeds
    /// back into itself: with perspective, changing the zoom moves the camera, which changes how
    /// big the projected graph is. Four passes converge well past the precision anyone can see.
    /// </summary>
    public void FitToContent()
    {
        var vm = Vm;
        double w = ActualWidth, h = ActualHeight;
        if (vm is null || w <= 0 || h <= 0) return;

        const double margin = 40;

        // The floating panels sit ON TOP of the canvas, so the space actually free for the graph
        // is narrower than the control. Centring on the whole width pushes the graph under the
        // filter panel, which is what made it look shoved to one side.
        double left  = FilterPanelWidth;
        double right = vm.HasGroups ? LegendPanelWidth : 0;

        double usableW = Math.Max(w - left - right - 2 * margin, 100);
        double usableH = Math.Max(h - 2 * margin, 100);
        double centreX = left + (w - left - right) / 2;   // middle of the free strip

        for (int pass = 0; pass < 4; pass++)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            bool any = false;

            foreach (var n in vm.Nodes)
            {
                if (!n.Visible) continue;
                var p = _camera.Project(n.X, n.Y, n.Z);
                if (p.Depth <= GraphCamera.NearPlane) continue;

                any = true;
                double r = Radius(n) * p.Scale + 20;    // room for the label under the disc
                if (p.X - r < minX) minX = p.X - r;
                if (p.X + r > maxX) maxX = p.X + r;
                if (p.Y - r < minY) minY = p.Y - r;
                if (p.Y + r > maxY) maxY = p.Y + r;
            }

            if (!any)
            {
                _camera.Zoom = 0.85; _ox = 90; _oy = 0;
                return;
            }

            double fit = Math.Min(usableW / Math.Max(maxX - minX, 1),
                                  usableH / Math.Max(maxY - minY, 1));

            // Filling the window is not the only goal. A small, tightly-linked vault would
            // otherwise be magnified until its hubs became overlapping blobs and the picture said
            // nothing, so the zoom is also capped by how big the LARGEST node may draw.
            _camera.Zoom = Math.Clamp(_camera.Zoom * fit, MinZoom, MaxFitZoom(vm));

            // Screen x = world·scale + w/2 + offset. Put the content's centre on the middle of the
            // free strip rather than the middle of the control.
            _ox = centreX - w / 2 - (minX + maxX) / 2;
            _oy = -(minY + maxY) / 2;
        }
    }

    /// <summary>
    /// Ceiling for the automatic framing: the zoom at which the best-connected visible note would
    /// draw at <see cref="MaxNodeScreenRadius"/>. Without it, a vault of thirty densely-linked
    /// notes gets zoomed until the hubs are overlapping discs filling the window — technically a
    /// perfect fit, and useless to look at.
    /// </summary>
    private double MaxFitZoom(GraphViewModel vm)
    {
        double largest = 0;
        foreach (var n in vm.Nodes)
            if (n.Visible) largest = Math.Max(largest, Radius(n));

        if (largest <= 0) return 1.0;

        // Bound by how big the node may DRAW, never by a fixed zoom number. An extra ceiling of
        // 1.0 used to sit here, left over from before this cap existed, and it quietly inverted
        // the size dial: shrinking the nodes also shrank the whole graph, because the framing was
        // no longer allowed to zoom in and use the space that had just been freed.
        return Math.Clamp(MaxNodeScreenRadius / largest, MinZoom, 4);
    }

    /// <summary>On-screen radius, in pixels, the biggest node should not exceed after auto-framing.</summary>
    private const double MaxNodeScreenRadius = 15;

    /// <summary>
    /// Room the floating overlays take out of the canvas. They are drawn on top of it, so the
    /// automatic framing has to steer the graph clear of them or it lands underneath.
    /// Kept in step with the panel widths in GraphView.xaml (232 + 14 margin, 200 + 14 margin).
    /// </summary>
    private const double FilterPanelWidth = 260;
    private const double LegendPanelWidth = 228;

    /// <summary>Recommended framing after a (re)build.</summary>
    private void OnGraphRebuilt()
    {
        _camera.Zoom = 0.85;
        _ox = 90;
        _oy = 0;
        _hover = null;
        _labelCache.Clear();
        _labelOutline.Clear();
        // Fitting NOW would frame the seed positions, not the layout — the simulation has not run
        // yet. Defer it to the moment the layout stops moving.
        _needsFit = true;
        Wake();
    }

    // ── Frame loop ──

    private void OnFrame(object? sender, EventArgs e)
    {
        // Skip work while the graph is collapsed (editor mode is active).
        if (!IsVisible || Vm is null) return;
        // Rendering stays on every frame even when the physics is frozen — hover highlighting and
        // camera moves still have to repaint, and drawing is the cheap half.
        if (!_settled) Tick();
        InvalidateVisual();
    }

    /// <summary>One step of the force simulation, in three dimensions.</summary>
    private void Tick()
    {
        var vm = Vm!;

        // Notes with no links are PLACED by GraphLayout, not simulated: nothing pulls on them, so
        // running them through the forces would only cost time and let them drift into the middle
        // of everything. Keeping them out also shrinks the expensive part of the frame, which in a
        // link-poor vault is most of the nodes.
        _simulated.Clear();
        foreach (var n in vm.Nodes)
        {
            if (!n.Visible) continue;
            if (n.Parked)
            {
                if (!n.Pinned)
                {
                    n.X = n.TargetX; n.Y = n.TargetY; n.Z = n.TargetZ;
                    n.Vx = n.Vy = n.Vz = 0;
                }
                continue;
            }
            _simulated.Add(n);
        }

        double repel   = 14000 * vm.ForceRepel;
        double linkK   = 0.02 * vm.ForceLink;
        double centerK = 0.006 * vm.ForceCenter;
        const double linkLen = 130;

        // ── Repulsion, via Barnes-Hut instead of every-pair ──
        _tree.Build(_simulated);
        for (int i = 0; i < _simulated.Count; i++)
        {
            _tree.Repulsion(i, repel, Theta, out double fx, out double fy, out double fz);
            var n = _simulated[i];
            n.Vx += fx * 0.0016;
            n.Vy += fy * 0.0016;
            n.Vz += fz * 0.0016;
        }

        // ── Collision ──
        // The count matters: the settle check below measures VELOCITY, and this pass corrects
        // POSITIONS without touching velocity. Ignoring it lets the simulation freeze on a picture
        // whose overlaps were never finished being pulled apart.
        int overlaps = _collision.Resolve(_simulated, CollisionPad, CollisionStiffness);

        // Spring force along links. Link endpoints always have a link, so none of them is parked.
        foreach (var l in vm.Links)
        {
            var a = l.Source; var b = l.Target;
            if (!a.Visible || !b.Visible) continue;
            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (d < 0.01) d = 0.01;
            double f = (d - linkLen) * linkK;
            double ux = dx / d, uy = dy / d, uz = dz / d;
            a.Vx += ux * f; a.Vy += uy * f; a.Vz += uz * f;
            b.Vx -= ux * f; b.Vy -= uy * f; b.Vz -= uz * f;
        }

        // Gravity + integration. With clustering on, "the centre" is not the origin but the spot
        // GraphLayout assigned this node — its island's place inside its folder. One force doing
        // the job instead of stacking a cluster force on top of a global pull that fights it.
        // In flat mode every target has Z = 0, which is what collapses the graph onto the plane.
        bool cluster = vm.ClusterByFolder;
        double energy = 0;

        foreach (var n in _simulated)
        {
            double tx = cluster ? n.TargetX : 0;
            double ty = cluster ? n.TargetY : 0;
            double tz = cluster ? n.TargetZ : 0;

            n.Vx += (tx - n.X) * centerK;
            n.Vy += (ty - n.Y) * centerK;
            n.Vz += (tz - n.Z) * centerK;

            if (n.Pinned)
            {
                n.X = n.Fx!.Value; n.Y = n.Fy!.Value; n.Z = n.Fz ?? n.Z;
                n.Vx = n.Vy = n.Vz = 0;
                continue;
            }
            n.Vx *= 0.82; n.Vy *= 0.82; n.Vz *= 0.82;
            n.X += n.Vx; n.Y += n.Vy; n.Z += n.Vz;
            energy += n.Vx * n.Vx + n.Vy * n.Vy + n.Vz * n.Vz;
        }

        // Once the layout stops moving there is nothing to recompute. Freezing here is what keeps
        // an open graph from burning a core forever on a picture that never changes; any input or
        // filter change calls Wake().
        //
        // "Stopped moving" is BOTH conditions: velocities near zero AND no overlaps left to undo.
        // Velocity alone is not enough, because the collision pass works on positions — a layout
        // can be perfectly still and still have discs sitting on top of each other.
        bool wasSettled = _settled;
        bool quiet = _simulated.Count > 0 && energy / _simulated.Count < SettleEnergy;

        _framesAwake++;

        // Safety valve. A dense enough graph can be over-constrained — more links than the space
        // can satisfy — and then the overlaps never reach zero. Rather than spin a core forever
        // chasing an impossible layout, give it a bounded effort and accept the result.
        _settled = (quiet && overlaps == 0) || _framesAwake > MaxFramesAwake;

        // The layout just came to rest — this is the first moment the real extent is known, so it
        // is the right moment to frame it.
        if (_settled && !wasSettled && _needsFit)
        {
            FitToContent();
            _needsFit = false;
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

        double cxo = w / 2 + _ox;
        double cyo = h / 2 + _oy;

        var active  = vm.ActiveFile;
        var focus   = _hover;
        var focusId = focus?.Id;
        var nbrs    = focus is null ? null : Neighbours(focus.Id);

        // Everything is drawn in screen pixels now, so pen widths are plain constants instead of
        // being divided by a global zoom.
        var penNormal = FrozenPen(LinkColor, 0.55, 1.0);
        var penDim    = FrozenPen(LinkColor, 0.25, 1.0);
        var penHot    = FrozenPen(LinkHot,   0.85, 1.6);
        var ringPen   = FrozenPen(Colors.White, 1.0, 2.0);
        var pinPen    = FrozenPen(PinRing, 0.9, 1.3);

        // Projection first: it measures how deep the graph actually reaches, and the distance
        // fading below is scaled against THAT rather than against the camera's absolute numbers.
        ProjectVisible(vm, cxo, cyo);

        // ── Links ──
        foreach (var l in vm.Links)
        {
            var a = l.Source; var b = l.Target;
            if (!a.Visible || !b.Visible) continue;

            var pa = _camera.Project(a.X, a.Y, a.Z);
            var pb = _camera.Project(b.X, b.Y, b.Z);
            // A line with one end behind the lens would be drawn wrapped around the screen.
            if (pa.Depth <= GraphCamera.NearPlane || pb.Depth <= GraphCamera.NearPlane) continue;

            bool hot = focusId is not null && (a.Id == focusId || b.Id == focusId);
            var pen = hot ? penHot : (focusId is not null ? penDim : penNormal);

            // Links fade with distance too. Left at full strength they form a bright mesh in
            // front of everything and flatten the depth the discs are trying to convey.
            dc.PushOpacity(Fog((pa.Depth + pb.Depth) / 2));
            dc.DrawLine(pen, new Point(pa.X + cxo, pa.Y + cyo), new Point(pb.X + cxo, pb.Y + cyo));
            dc.Pop();
        }

        // ── Nodes, painted back to front ──
        // No depth buffer in immediate-mode 2D drawing, so the painter's algorithm does the job:
        // draw the far ones first and let the near ones cover them.
        foreach (int i in _order)
        {
            var p = _projected[i];
            var n = p.Node;

            double r = Math.Max(Radius(n) * p.Scale, 1);
            bool isActive = n.Id == active;
            bool dim = focusId is not null && n.Id != focusId && !(nbrs is not null && nbrs.Contains(n.Id));

            // Distance fading. Shrinking alone is a weak depth cue — a small disc reads as a note
            // with few links, not as a far one — so distance drains the colour as well.
            //
            // The colour is BLENDED toward the background rather than made transparent. A
            // see-through disc shows the link mesh running behind it and turns muddy; a blended
            // one stays solid and simply looks further away. That is how haze works on a real
            // horizon, and it is the cue the eye already knows how to read.
            double fog = Fog(p.Depth);

            Brush fill = isActive ? NodeActive : GroupBrush(n.Group);
            if (n.Id == focusId) fill = NodeHover;
            else if (!isActive)  fill = Faded(fill, fog);

            if (dim) dc.PushOpacity(0.28);

            dc.DrawEllipse(fill, isActive ? ringPen : null, new Point(p.Sx, p.Sy), r, r);
            // Halo around nodes the user parked by hand, so a fixed layout stays readable.
            if (n.Pinned)
                dc.DrawEllipse(null, pinPen, new Point(p.Sx, p.Sy), r + 3.5, r + 3.5);

            if (dim) dc.Pop();
        }

        // ── Labels (own pass: they must sit above every disc, and they compete for space) ──
        if (vm.ShowLabels)
            DrawLabels(dc, labelBrush, active, focusId, nbrs);
    }

    /// <summary>
    /// How much of its own colour a node keeps, from 1 for the nearest thing on screen down to
    /// <c>1 - FogStrength</c> for the furthest.
    ///
    /// Normalised against the depth the graph ACTUALLY occupies, not against the camera's absolute
    /// distance — that was the bug in the first attempt. Tying it to the raw perspective ratio
    /// meant a graph 440 units deep seen from 1700 away spanned only 0.89 to 1.0: an eleven per
    /// cent difference nobody can see. Measuring the real span guarantees the full range is used
    /// however deep or shallow the layout turns out to be.
    ///
    /// A flat layout has no span at all, so this returns 1 everywhere and the effect switches
    /// itself off — no mode flag needed.
    /// </summary>
    private double Fog(double depth)
    {
        double span = _farDepth - _nearDepth;
        if (span < 1) return 1;

        double howFar = Math.Clamp((depth - _nearDepth) / span, 0, 1);
        return 1 - howFar * FogStrength;
    }

    /// <summary>
    /// Blends a brush toward the background by <paramref name="fog"/>, the way haze washes out a
    /// distant hillside. Results are cached per (palette slot, fog step): a brush per node per
    /// frame would be pure allocation churn, and the eye cannot tell the steps apart anyway.
    /// </summary>
    private Brush Faded(Brush brush, double fog)
    {
        if (fog >= 0.999 || brush is not SolidColorBrush solid) return brush;

        var background = (TryFindResource("GraphBackground") as SolidColorBrush)?.Color ?? Colors.Black;
        if (background != _fogBackground)
        {
            _fadedBrushes.Clear();
            _fogBackground = background;
        }

        int step = (int)Math.Round(fog * FogSteps);
        long key = ((long)solid.Color.R << 32) | ((long)solid.Color.G << 24)
                 | ((long)solid.Color.B << 16) | (uint)step;

        if (_fadedBrushes.TryGetValue(key, out var cached)) return cached;

        double keep = (double)step / FogSteps;
        var faded = new SolidColorBrush(Color.FromRgb(
            (byte)(solid.Color.R * keep + background.R * (1 - keep)),
            (byte)(solid.Color.G * keep + background.G * (1 - keep)),
            (byte)(solid.Color.B * keep + background.B * (1 - keep))));
        faded.Freeze();

        _fadedBrushes[key] = faded;
        return faded;
    }

    /// <summary>
    /// Projects every visible node into <see cref="_projected"/> and fills <see cref="_order"/>
    /// with indices sorted far-to-near.
    /// </summary>
    private void ProjectVisible(GraphViewModel vm, double cxo, double cyo)
    {
        _projected.Clear();
        _order.Clear();
        _nearDepth = double.MaxValue;
        _farDepth  = double.MinValue;

        foreach (var n in vm.Nodes)
        {
            if (!n.Visible) continue;
            var p = _camera.Project(n.X, n.Y, n.Z);
            if (p.Depth <= GraphCamera.NearPlane) continue;   // behind the camera

            if (p.Depth < _nearDepth) _nearDepth = p.Depth;
            if (p.Depth > _farDepth)  _farDepth  = p.Depth;

            _order.Add(_projected.Count);
            _projected.Add(new Projected
            {
                Node  = n,
                Sx    = p.X + cxo,
                Sy    = p.Y + cyo,
                Depth = p.Depth,
                Scale = p.Scale
            });
        }

        if (_projected.Count == 0) { _nearDepth = 0; _farDepth = 0; }

        var projected = _projected;
        _order.Sort((a, b) => projected[b].Depth.CompareTo(projected[a].Depth));
    }

    /// <summary>
    /// Paints the note names. Drawing every one of them turns a mid-size vault into unreadable
    /// soup, so the labels are laid out greedily BY IMPORTANCE — the active note first, then the
    /// hovered node and its neighbours, then the best-connected notes — and any label that would
    /// land on top of one already placed is dropped for this frame.
    ///
    /// Text is NOT scaled with distance. A far note drawn with tiny type would be unreadable and
    /// still eat the space, so distance instead decides who WINS the space: among equals, the
    /// nearer node gets its name.
    /// </summary>
    private void DrawLabels(
        DrawingContext dc,
        Brush labelBrush,
        string active,
        string? focusId,
        HashSet<string>? nbrs)
    {
        const double em = 11.0;
        EnsureLabelCache(em, labelBrush);

        // Names land on top of the discs, and pale text over a pale node is unreadable however
        // correct the draw order is. Outlining each glyph in the BACKGROUND colour carves the text
        // out of whatever sits behind it — the trick a map uses for place names over terrain.
        // Reading the colour from the theme resource keeps it right in both light and dark.
        var background = (TryFindResource("GraphBackground") as SolidColorBrush)?.Color ?? Colors.Black;
        var haloBrush = new SolidColorBrush(Color.FromArgb(215, background.R, background.G, background.B));
        haloBrush.Freeze();
        var haloPen = new Pen(haloBrush, HaloThickness)
        {
            LineJoin = PenLineJoin.Round   // square joins spit spikes out of sharp glyph corners
        };
        haloPen.Freeze();

        _labelOrder.Clear();
        foreach (int i in _order)
        {
            var n = _projected[i].Node;
            bool isActive = n.Id == active;
            bool isFocus  = n.Id == focusId;

            // Below this zoom only the notes the user is pointing at keep their name.
            if (_camera.Zoom <= 0.55 && !isActive && !isFocus) continue;
            // Off-screen labels would still claim space in the occlusion pass.
            if (_projected[i].Sx < -200 || _projected[i].Sx > ActualWidth + 200) continue;
            if (_projected[i].Sy < -200 || _projected[i].Sy > ActualHeight + 200) continue;

            _labelOrder.Add(i);
        }

        _labelOrder.Sort((a, b) => Priority(b).CompareTo(Priority(a)));

        _placed.Clear();

        foreach (int i in _labelOrder)
        {
            var p = _projected[i];
            var n = p.Node;
            if (!_labelCache.TryGetValue(n.Id, out var ft)) continue;

            double r = Math.Max(Radius(n) * p.Scale, 1);
            var rect = new Rect(p.Sx - ft.Width / 2, p.Sy + r + 6, ft.Width, ft.Height);

            var probe = rect;
            probe.Inflate(2, 2);
            bool blocked = false;
            foreach (var taken in _placed)
                if (taken.IntersectsWith(probe)) { blocked = true; break; }
            if (blocked) continue;

            _placed.Add(rect);

            bool dim = focusId is not null && n.Id != focusId && !(nbrs is not null && nbrs.Contains(n.Id));
            double fog = Math.Max(Fog(p.Depth), 0.45);   // never fade a name past reading
            dc.PushOpacity((dim ? 0.3 : 0.92) * fog);

            if (_labelOutline.TryGetValue(n.Id, out var outline))
            {
                // The glyph outline is cached at the origin, so the position is a transform rather
                // than a rebuild — BuildGeometry is far too expensive to run per label per frame.
                dc.PushTransform(new TranslateTransform(rect.X, rect.Y));

                // TWO passes, and the order is the whole point. A stroke in WPF straddles the path
                // — half outside, half INSIDE — and at this text size the inner half is wider than
                // the stems of the letters. Filling and stroking in one call therefore paints the
                // halo straight over the glyph and the name vanishes. Stroke first as a backdrop,
                // then lay the fill on top so it covers the inward half.
                dc.DrawGeometry(null, haloPen, outline);
                dc.DrawGeometry(labelBrush, null, outline);

                dc.Pop();
            }
            else
            {
                dc.DrawText(ft, rect.TopLeft);
            }

            dc.Pop();
        }

        double Priority(int index)
        {
            var p = _projected[index];
            var n = p.Node;
            if (n.Id == active || n.Id == focusId) return 1e9;
            double bonus = Math.Clamp(p.Scale / _camera.Zoom, 0, 3);   // nearer wins a tie
            if (nbrs is not null && nbrs.Contains(n.Id)) return 1e6 + n.Degree * 10 + bonus;
            return n.Degree * 10 + bonus;
        }
    }

    private static readonly Typeface _uiTypeface = new("Segoe UI");

    // ── Label cache ──
    // FormattedText is expensive to build, and rebuilding one per node per frame is pure waste.
    // Since labels are drawn at a constant pixel size now, the cache only has to be dropped when
    // the theme brush changes or the graph is rebuilt.
    // NOTE: `new()`, not a collection expression — Dictionary<K,V>.Add takes two arguments, so it
    // is not a valid collection-expression target in C# 12 (CS9174).
    private readonly Dictionary<string, FormattedText> _labelCache = new();
    // Glyph outlines, built once at the origin and positioned with a transform. BuildGeometry is
    // expensive enough that calling it per label per frame would cost more than everything else
    // in the frame put together.
    private readonly Dictionary<string, Geometry> _labelOutline = new();
    private readonly List<int>  _labelOrder = [];
    private readonly List<Rect> _placed     = [];
    private double _cachedEm = -1;
    private Brush? _cachedBrush;

    /// <summary>
    /// Width of the outline carved around label text. Only HALF of it ends up visible: a stroke
    /// straddles the path, and the inward half is covered by the fill painted over it.
    /// </summary>
    private const double HaloThickness = 3.0;

    private void EnsureLabelCache(double em, Brush brush)
    {
        if (Math.Abs(em - _cachedEm) > 0.0001 || !ReferenceEquals(brush, _cachedBrush))
        {
            _labelCache.Clear();
            _labelOutline.Clear();
            _cachedEm    = em;
            _cachedBrush = brush;
        }

        if (Vm is null) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var n in Vm.Nodes)
        {
            if (!n.Visible || _labelCache.ContainsKey(n.Id)) continue;

            var text = new FormattedText(
                n.Label,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                _uiTypeface,
                em,
                brush,
                dpi);

            _labelCache[n.Id] = text;

            var outline = text.BuildGeometry(new Point(0, 0));
            outline.Freeze();
            _labelOutline[n.Id] = outline;
        }
    }

    // ── Model helpers ──

    private static double Radius(GraphNode n) => n.Radius;

    // Frozen brush per palette slot. Built lazily and kept for the life of the app: there are at
    // most GraphPalette.Count of them and they are immutable, so re-creating one per frame would
    // be pure allocation churn. UI thread only.
    private static readonly Dictionary<int, Brush> _groupBrushes = new();

    private static Brush GroupBrush(int groupIndex)
    {
        int slot = groupIndex < 0 ? -1 : groupIndex % GraphPalette.Count;
        if (!_groupBrushes.TryGetValue(slot, out var brush))
            _groupBrushes[slot] = brush = Frozen(GraphPalette.HexFor(groupIndex));
        return brush;
    }

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

    /// <summary>
    /// The node under the pointer. With perspective, several nodes can cover the same pixel, so
    /// the NEAREST to the camera wins — clicking has to hit what you can actually see.
    /// </summary>
    private GraphNode? Pick(Point pointer)
    {
        if (Vm is null) return null;

        double cxo = ActualWidth / 2 + _ox;
        double cyo = ActualHeight / 2 + _oy;

        GraphNode? best = null;
        double bestDepth = double.MaxValue;

        foreach (var n in Vm.Nodes)
        {
            if (!n.Visible) continue;
            var p = _camera.Project(n.X, n.Y, n.Z);
            if (p.Depth <= GraphCamera.NearPlane || p.Depth >= bestDepth) continue;

            double sx = p.X + cxo, sy = p.Y + cyo;
            double r  = Radius(n) * p.Scale + 6;
            double dx = sx - pointer.X, dy = sy - pointer.Y;

            if (dx * dx + dy * dy <= r * r)
            {
                bestDepth = p.Depth;
                best = n;
            }
        }

        return best;
    }

    // ── Input ──

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var pointer = e.GetPosition(this);
        _lastPointer = pointer;
        _moved = 0;

        var node = Pick(pointer);
        if (node is not null)
        {
            _dragNode = node;
            _dragWasPinned = node.Pinned;
            // The depth the node is dragged ON. A mouse position is a ray, not a point: without
            // fixing a depth there is no single world position it could mean.
            _dragDepth = _camera.DepthOf(node.X, node.Y, node.Z);
            node.Fx = node.X; node.Fy = node.Y; node.Fz = node.Z;
            Cursor = Cursors.SizeAll;
        }
        else if (Vm?.ThreeD == true && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            _orbiting = true;
            Cursor = Cursors.ScrollAll;
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
        double dx = p.X - _lastPointer.X;
        double dy = p.Y - _lastPointer.Y;

        if (_dragNode is not null)
        {
            var (wx, wy, wz) = _camera.Unproject(
                p.X - (ActualWidth / 2 + _ox),
                p.Y - (ActualHeight / 2 + _oy),
                _dragDepth);

            _dragNode.Fx = wx; _dragNode.Fy = wy; _dragNode.Fz = wz;
            _dragNode.X  = wx; _dragNode.Y  = wy; _dragNode.Z  = wz;
            _moved += Math.Abs(dx) + Math.Abs(dy);
            Wake();   // dragging one node shoves its neighbours around
        }
        else if (_orbiting)
        {
            _camera.Yaw   += dx * OrbitSpeed;
            _camera.Pitch  = Math.Clamp(_camera.Pitch + dy * OrbitSpeed, -MaxPitch, MaxPitch);
            _moved += Math.Abs(dx) + Math.Abs(dy);
        }
        else if (_panning)
        {
            _ox += dx;
            _oy += dy;
            _moved += Math.Abs(dx) + Math.Abs(dy);
        }
        else
        {
            _hover = Pick(p);
            Cursor = _hover is not null ? Cursors.Hand : Cursors.Arrow;
        }

        _lastPointer = p;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragNode is not null)
        {
            if (_moved < 4)
            {
                // A plain click opens the note and must leave the pin state untouched.
                if (!_dragWasPinned) _dragNode.Unpin();
                Vm?.RequestOpen(_dragNode.Id);
            }
            // A real drag KEEPS Fx/Fy/Fz: the node stays exactly where it was dropped until
            // the user releases it (right-click on it, or the "liberar nodos" button).
            Wake();   // released or re-parked, the neighbourhood has to re-settle
        }

        _dragNode = null;
        _panning  = false;
        _orbiting = false;
        Cursor = Cursors.Arrow;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <summary>Capture can be lost mid-drag (Alt+Tab, a modal dialog). Drop the gesture so the
    /// node is not dragged around afterwards without a button held; it keeps its current pin.</summary>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _dragNode = null;
        _panning  = false;
        _orbiting = false;
        Cursor = Cursors.Arrow;
    }

    /// <summary>Right-click on a pinned node releases it back into the simulation.</summary>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        var node = Pick(e.GetPosition(this));
        if (node is not null && node.Pinned)
        {
            node.Unpin();
            Wake();
            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        double f = e.Delta > 0 ? 1.12 : 1 / 1.12;
        _camera.Zoom = Math.Max(MinZoom, Math.Min(4, _camera.Zoom * f));
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_dragNode is null && !_panning && !_orbiting) _hover = null;
    }

    /// <summary>
    /// Brings the simulation out of its settled state. Anything that can change where a node
    /// belongs must call this, or the graph would stay frozen on a now-wrong picture.
    /// </summary>
    public void Wake()
    {
        _settled = false;
        _framesAwake = 0;
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
