using MarkdownVault.Models;

namespace MarkdownVault.Services;

/// <summary>
/// Decides how big each node is drawn. One place, because the renderer, the collision pass and
/// the camera framing all have to agree — two copies would drift, and the symptom would be discs
/// that overlap no matter how hard the collision pushes.
///
/// The size is RELATIVE to the vault, not an absolute function of the link count. An absolute
/// formula has to assume a range of degrees, and vaults disagree wildly about that: a loose pile
/// of notes tops out around five links, while a dense wiki reaches thirty or more. Tuned for the
/// first, the second draws nodes so large they swallow the space between them and the graph
/// becomes one solid blob — and zooming out does not help, because everything shrinks together
/// and the PROPORTION is what is wrong.
///
/// Normalising against the busiest note in the vault keeps the meaning ("bigger = better
/// connected") while guaranteeing the picture stays legible whatever the vault looks like.
/// </summary>
public static class GraphMetrics
{
    /// <summary>World-space radius of a note nothing links to.</summary>
    public const double MinRadius = 4;

    /// <summary>
    /// World-space radius of the busiest note in the vault. Kept well under the spring length
    /// (130) so neighbours have visible space between them instead of touching.
    /// </summary>
    public const double MaxRadius = 13;

    /// <summary>Narrowest and widest the user's size dial may go.</summary>
    public const double MinScale = 0.2;
    public const double MaxScale = 2.5;

    /// <summary>
    /// Stamps <see cref="GraphNode.Radius"/> on every node, scaled against the most-linked one and
    /// then by the user's <paramref name="scale"/>. Call once per graph build, or when the dial
    /// moves — never per frame.
    ///
    /// The curve is a square root, so area grows roughly linearly with links: a hub reads as
    /// clearly bigger without a note with thirty links becoming a planet beside one with three.
    ///
    /// The user's dial is applied HERE rather than at draw time on purpose. The radius is what the
    /// collision pass keeps apart and what the camera framing measures, so scaling only the drawing
    /// would grow the discs without granting them any more room — and they would overlap.
    /// </summary>
    public static void Assign(IReadOnlyList<GraphNode> nodes, double scale = 1.0)
    {
        scale = Math.Clamp(scale, MinScale, MaxScale);

        int busiest = 0;
        foreach (var n in nodes) busiest = Math.Max(busiest, n.Degree);

        foreach (var n in nodes)
        {
            double baseRadius = busiest <= 0
                ? MinRadius
                : MinRadius + (MaxRadius - MinRadius) * Math.Sqrt((double)n.Degree / busiest);

            n.Radius = baseRadius * scale;
        }
    }
}
