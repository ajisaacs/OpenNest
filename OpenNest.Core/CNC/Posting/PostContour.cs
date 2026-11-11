using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.CNC.Posting;

/// <summary>
/// One contour of a posted part: pierce, lead-in and cut path in a single absolute frame
/// (sheet coordinates from <see cref="PostContours.Extract(Part)"/>). Arc moves keep their
/// absolute centres; each post formats I/J its own way.
/// </summary>
public sealed class PostContour
{
    /// <summary>Pierce point: the rapid target.</summary>
    public Vector Pierce { get; set; }

    /// <summary>
    /// First lead-in move, ending on the contour, or null when the contour starts cutting at
    /// the pierce.
    /// </summary>
    public Motion LeadIn { get; set; }

    /// <summary>Lead-in moves after the first (e.g. the arc after a clean-hole line).</summary>
    public List<Motion> LeadInExtra { get; } = new();

    /// <summary>Moves made with the cut layer on: cut, lead-out, display and scribe.</summary>
    public List<Motion> Cuts { get; } = new();

    /// <summary>
    /// Head-height lock changes in a cut-off's overcut: each comes before the cut at
    /// <c>Before</c> (<see cref="Cuts"/>.Count: after the last cut), locking when <c>Lock</c>.
    /// </summary>
    public List<(int Before, bool Lock)> HeadLocks { get; } = new();

    /// <summary>True for the part's outside edge; false for holes, marks and cut-offs.</summary>
    public bool IsPerimeter { get; set; }

    /// <summary>
    /// The compensation that keeps the kerf in the scrap: <see cref="KerfType.Left"/> (G41)
    /// leaves the part right of travel, <see cref="KerfType.Right"/> (G42) left of it.
    /// <see cref="KerfType.None"/> for marks and cut-offs, which have no inside.
    /// </summary>
    public KerfType Kerf { get; set; }

    /// <summary>True when every cut is on the scribe layer: a mark, not material.</summary>
    public bool IsMark
    {
        get
        {
            if (Cuts.Count == 0)
                return false;
            foreach (var cut in Cuts)
            {
                if (PostContours.LayerOf(cut) != LayerType.Scribe)
                    return false;
            }
            return true;
        }
    }
}
