namespace MarkdownVault.Services;

/// <summary>
/// Where a world point lands on screen. <c>X</c>/<c>Y</c> are pixels relative to the centre of the
/// canvas (the caller adds its own centre and pan); <c>Depth</c> is the distance in front of the
/// camera; <c>Scale</c> is how much the perspective divide shrank it.
/// </summary>
public readonly record struct GraphProjection(double X, double Y, double Depth, double Scale);

/// <summary>
/// The orbit camera for the graph: yaw and pitch around the origin, at a fixed distance, with a
/// perspective divide.
///
/// It lives in the services layer rather than inside the canvas for one reason: projecting and
/// UN-projecting must be exact inverses. Dragging a node means taking a mouse position plus a
/// depth and turning it back into a world point — get the inverse subtly wrong and nodes slide
/// away from the cursor in a way that looks like a physics bug and is impossible to chase. Out
/// here it is a round-trip assertion in a test.
///
/// Flat mode is not a special case: with <see cref="Yaw"/> and <see cref="Pitch"/> at zero and
/// every node at Z = 0, <c>Depth</c> is constant, <c>Scale</c> equals <see cref="Zoom"/>, and the
/// projection degenerates into exactly the 2D camera the graph had before.
/// </summary>
public sealed class GraphCamera
{
    /// <summary>
    /// Focal length in world units. Sets how strong the perspective is: the camera sits at
    /// <c>Focal / Zoom</c>, so a bigger focal length means a more distant camera and a flatter,
    /// more orthographic picture.
    /// </summary>
    public const double Focal = 1200;

    /// <summary>Anything closer than this is behind (or on top of) the lens and cannot be drawn.</summary>
    public const double NearPlane = 1;

    /// <summary>Rotation around the vertical axis, in radians.</summary>
    public double Yaw { get; set; }

    /// <summary>Rotation around the horizontal axis, in radians. Clamped by the caller to avoid flipping.</summary>
    public double Pitch { get; set; }

    /// <summary>
    /// On-screen pixels per world unit AT THE PIVOT PLANE. Everything nearer than the pivot draws
    /// bigger and everything further draws smaller; this is the reference the pen widths, the label
    /// sizes and the zoom buttons all work in, so the 2D code that used to divide by the zoom keeps
    /// meaning the same thing.
    /// </summary>
    public double Zoom { get; set; } = 0.85;

    /// <summary>Distance from the camera to the pivot, derived from <see cref="Zoom"/>.</summary>
    public double Distance => Focal / Zoom;

    /// <summary>Projects a world point. Check <c>Depth &gt; NearPlane</c> before drawing it.</summary>
    public GraphProjection Project(double x, double y, double z)
    {
        ToCamera(x, y, z, out double cx, out double cy, out double cz);

        double depth = cz + Distance;
        double scale = Focal / Math.Max(depth, NearPlane);

        return new GraphProjection(cx * scale, cy * scale, depth, scale);
    }

    /// <summary>
    /// The inverse of <see cref="Project"/>: takes a point on screen plus the depth it should sit
    /// at, and returns the world position that projects exactly there. This is what makes dragging
    /// work — a mouse position alone is a ray, not a point, so the caller supplies the depth of the
    /// node being dragged and gets back the world point on that plane.
    ///
    /// Only an inverse for <paramref name="depth"/> greater than <see cref="NearPlane"/>. Behind
    /// the lens there is nothing to invert; the depth is clamped and the answer is meaningless.
    /// Callers already hold to this: <c>Pick</c> refuses nodes at or behind the near plane, so a
    /// node can never be dragged from there.
    /// </summary>
    public (double X, double Y, double Z) Unproject(double screenX, double screenY, double depth)
    {
        double safeDepth = Math.Max(depth, NearPlane);
        double scale = Focal / safeDepth;

        double cx = screenX / scale;
        double cy = screenY / scale;
        double cz = safeDepth - Distance;

        return FromCamera(cx, cy, cz);
    }

    /// <summary>Depth of a world point without computing the rest of the projection.</summary>
    public double DepthOf(double x, double y, double z)
    {
        ToCamera(x, y, z, out _, out _, out double cz);
        return cz + Distance;
    }

    // ── World ↔ camera rotation ──
    // Yaw first (around Y), then pitch (around X). Unproject applies the transposes in the
    // opposite order, which is what makes the pair exact inverses.

    private void ToCamera(double x, double y, double z, out double cx, out double cy, out double cz)
    {
        double cosYaw = Math.Cos(Yaw),   sinYaw = Math.Sin(Yaw);
        double cosPit = Math.Cos(Pitch), sinPit = Math.Sin(Pitch);

        double x1 =  x * cosYaw + z * sinYaw;
        double z1 = -x * sinYaw + z * cosYaw;

        cx = x1;
        cy = y * cosPit - z1 * sinPit;
        cz = y * sinPit + z1 * cosPit;
    }

    private (double X, double Y, double Z) FromCamera(double cx, double cy, double cz)
    {
        double cosYaw = Math.Cos(Yaw),   sinYaw = Math.Sin(Yaw);
        double cosPit = Math.Cos(Pitch), sinPit = Math.Sin(Pitch);

        double y1 =  cy * cosPit + cz * sinPit;
        double z1 = -cy * sinPit + cz * cosPit;

        double x = cx * cosYaw - z1 * sinYaw;
        double z = cx * sinYaw + z1 * cosYaw;

        return (x, y1, z);
    }
}
