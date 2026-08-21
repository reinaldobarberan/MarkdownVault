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

    // ── Physics state (world coordinates) ──
    public double X;
    public double Y;
    public double Vx;
    public double Vy;

    /// <summary>Number of links touching this node (drives its radius).</summary>
    public int Degree;

    // ── Pin state (null = free). Set while dragging and KEPT after the drop, so a node
    //    the user moved stays where it was left instead of being sucked back by the forces.
    //    Cleared by right-clicking the node or by "liberar nodos" in the graph view.
    public double? Fx;
    public double? Fy;

    /// <summary>Whether the node holds a user-set position that the simulation must not touch.</summary>
    public bool Pinned => Fx is not null;

    /// <summary>Releases the node back to the force simulation from its current position.</summary>
    public void Unpin() => Fx = Fy = null;

    /// <summary>Whether the node passes the current search / local-graph filter.</summary>
    public bool Visible = true;
}

/// <summary>Undirected edge between two notes.</summary>
public sealed class GraphLink
{
    public required GraphNode Source { get; init; }
    public required GraphNode Target { get; init; }
}
