using MarkdownVault.Services;

namespace MarkdownVault.Models;

/// <summary>
/// Runtime node in the vault graph: one note plus its live physics state.
/// Mutable by design — the force-simulation loop updates position/velocity
/// in place every frame, so this is a plain class (not an ObservableObject).
/// </summary>
public sealed class GraphNode
{
    /// <summary>Vault-relative path with forward slashes (e.g. <c>sub/note.md</c>). Stable identity.</summary>
    public required string Id { get; init; }

    /// <summary>Display label: filename without extension.</summary>
    public required string Label { get; init; }

    /// <summary>Absolute path on disk, used to open the file.</summary>
    public required string FullPath { get; init; }

    /// <summary>
    /// Vault-relative folder holding the note, forward-slashed; empty for notes at the vault root.
    /// Derived from <see cref="Id"/> rather than stored, so there is exactly one source of truth
    /// for where a note lives.
    /// </summary>
    public string Folder
    {
        get
        {
            int cut = Id.LastIndexOf('/');
            return cut < 0 ? string.Empty : Id[..cut];
        }
    }

    /// <summary>
    /// First path segment of <see cref="Folder"/> — the grouping unit for the graph. Deep nesting
    /// would produce dozens of near-empty groups; the top folder is what reads as an "area" of
    /// the vault. Empty for notes at the root.
    /// </summary>
    public string TopFolder
    {
        get
        {
            var folder = Folder;
            if (folder.Length == 0) return string.Empty;
            int cut = folder.IndexOf('/');
            return cut < 0 ? folder : folder[..cut];
        }
    }

    /// <summary>
    /// Index of the group this note belongs to, assigned by <c>GraphGrouping</c>. Drives its
    /// colour and, when clustering is on, which anchor it gravitates to. -1 = ungrouped.
    /// </summary>
    public int Group = -1;

    /// <summary>
    /// Connected component ("island") this note belongs to, assigned by <c>GraphComponents</c>.
    /// <c>GraphComponents.Orphan</c> (-1) for notes with no links at all.
    /// </summary>
    public int Component = -1;

    // ── Layout target (world coordinates) ──
    // Where gravity pulls this node, stamped by GraphLayout. For a linked note it is its island's
    // spot inside its folder; for an orphan it is its fixed seat on the outer ring.
    public double TargetX;
    public double TargetY;
    public double TargetZ;

    /// <summary>
    /// True for notes the layout places by hand instead of simulating. Nothing pulls on a note
    /// with no links, so running it through the force loop only costs time and lets it drift into
    /// the middle of everything else — it gets a fixed seat and is skipped by the simulation.
    /// </summary>
    public bool Parked;

    // ── Physics state (world coordinates) ──
    // Z is the depth axis. In flat mode every node is pulled to Z = 0 and the view renders exactly
    // as it did before 3D existed, so the two modes share one simulation instead of two code paths.
    public double X;
    public double Y;
    public double Z;
    public double Vx;
    public double Vy;
    public double Vz;

    /// <summary>Number of links touching this node (drives its radius).</summary>
    public int Degree;

    /// <summary>
    /// World-space radius, stamped by <c>GraphMetrics.Assign</c> once per build. Scaled against
    /// the busiest note in the vault rather than computed from <see cref="Degree"/> alone, so a
    /// densely linked wiki does not draw its hubs large enough to swallow the space between them.
    /// </summary>
    public double Radius = GraphMetrics.MinRadius;

    // ── Pin state (null = free). Set while dragging and KEPT after the drop, so a node
    //    the user moved stays where it was left instead of being sucked back by the forces.
    //    Cleared by right-clicking the node or by "liberar nodos" in the graph view.
    public double? Fx;
    public double? Fy;
    public double? Fz;

    /// <summary>Whether the node holds a user-set position that the simulation must not touch.</summary>
    public bool Pinned => Fx is not null;

    /// <summary>Releases the node back to the force simulation from its current position.</summary>
    public void Unpin() => Fx = Fy = Fz = null;

    /// <summary>Whether the node passes the current search / local-graph filter.</summary>
    public bool Visible = true;
}

/// <summary>Undirected edge between two notes.</summary>
public sealed class GraphLink
{
    public required GraphNode Source { get; init; }
    public required GraphNode Target { get; init; }
}
