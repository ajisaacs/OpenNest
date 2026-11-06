using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.Diagnostics;

/// <summary>An owned diagnostic result. An empty Pairs list is clear only if IsComplete is true.</summary>
public sealed class PlateOverlapReport
{
    internal PlateOverlapReport(List<PlateOverlapPair> pairs, List<PlateOverlapIssue> issues,
        PlateOverlapSnapshot snapshot, List<PlateOverlapPair> clampFindings = null,
        List<CutOffClampFinding> cutOffClampFindings = null, List<PlateSpacingPair> spacingPairs = null,
        List<PlateOverlapIssue> spacingIssues = null, List<PlateEdgeFinding> edgeFindings = null)
    {
        Pairs = pairs.AsReadOnly();
        Issues = issues.AsReadOnly();
        Snapshot = snapshot;
        ClampFindings = (clampFindings ?? new List<PlateOverlapPair>()).AsReadOnly();
        CutOffClampFindings = (cutOffClampFindings ?? new List<CutOffClampFinding>()).AsReadOnly();
        SpacingPairs = (spacingPairs ?? new List<PlateSpacingPair>()).AsReadOnly();
        SpacingIssues = (spacingIssues ?? new List<PlateOverlapIssue>()).AsReadOnly();
        EdgeFindings = (edgeFindings ?? new List<PlateEdgeFinding>()).AsReadOnly();
    }

    /// <summary>The owned input this report was computed from; the baseline for incremental rechecks.</summary>
    internal PlateOverlapSnapshot Snapshot { get; }

    public IReadOnlyList<PlateOverlapPair> Pairs { get; }
    public IReadOnlyList<PlateOverlapIssue> Issues { get; }

    /// <summary>
    /// Part-vs-clamp-zone findings (C1): the same owned-fragment shape as part pairs, with a
    /// negative <see cref="PlateOverlapPair.PartBId"/> slot encoding the zone ordinal. Findings
    /// are reported like other overlap findings; they do not change <see cref="IsComplete"/>.
    /// </summary>
    public IReadOnlyList<PlateOverlapPair> ClampFindings { get; }
    /// <summary>Nominal cut-off/clamp contacts. Lines have no material area or part pair.</summary>
    public IReadOnlyList<CutOffClampFinding> CutOffClampFindings { get; }

    /// <summary>
    /// Pairs that share no material but sit closer than the plate's part spacing. Only a plate
    /// capture carries the spacing; see <see cref="SpacingChecked"/>.
    /// </summary>
    public IReadOnlyList<PlateSpacingPair> SpacingPairs { get; }

    /// <summary>
    /// Parts or pairs the spacing scan could not read. Kept apart from <see cref="Issues"/>,
    /// which incremental rechecks reuse by overlap pair.
    /// </summary>
    public IReadOnlyList<PlateOverlapIssue> SpacingIssues { get; }

    /// <summary>
    /// Parts off the sheet or inside its edge spacing, judged on clean material. Only a plate
    /// capture carries the sheet; a bare group of parts has none. Unreadable parts join
    /// <see cref="SpacingIssues"/>.
    /// </summary>
    public IReadOnlyList<PlateEdgeFinding> EdgeFindings { get; }

    /// <summary>True when the input required a gap, so empty SpacingPairs means something.</summary>
    public bool SpacingChecked => PartSpacingReview.Enforces(Snapshot.PartSpacing);

    public bool IsComplete => Issues.Count == 0 && SpacingIssues.Count == 0;
    public double ChordTolerance => PlateOverlapAnalyzer.ChordTolerance;
}

/// <summary>Shared material for two input positions, ordered by zero-based input index.</summary>
public sealed class PlateOverlapPair
{
    private readonly Box bounds;

    private PlateOverlapPair(PlateOverlapPair source, int partAId, int partBId, string partAName,
        string partBName)
    {
        PartAId = partAId;
        PartBId = partBId;
        PartAName = partAName;
        PartBName = partBName;
        Regions = source.Regions;
        Area = source.Area;
        Centroid = source.Centroid;
        Marker = source.Marker;
        bounds = source.bounds;
    }

    /// <summary>The same immutable geometry under new input positions and captured names.</summary>
    internal PlateOverlapPair Renumber(int partAId, int partBId, string partAName, string partBName) =>
        new(this, partAId, partBId, partAName, partBName);

    internal PlateOverlapPair(int partAId, int partBId, string partAName, string partBName,
        List<PlateOverlapRegion> regions, Vector centroid, Vector marker)
    {
        PartAId = partAId;
        PartBId = partBId;
        PartAName = partAName;
        PartBName = partBName;
        Regions = regions.AsReadOnly();
        Area = regions.Sum(region => region.Area);
        Centroid = centroid;
        Marker = marker;
        var points = regions.SelectMany(region => region.Vertices).ToArray();
        var left = points.Min(point => point.X);
        var bottom = points.Min(point => point.Y);
        bounds = new Box(left, bottom, points.Max(point => point.X) - left,
            points.Max(point => point.Y) - bottom);
    }

    public int PartAId { get; }
    public int PartBId { get; }
    public string PartAName { get; }
    public string PartBName { get; }
    /// <summary>Connected islands of shared material; no mutable kernel polygons are exposed.</summary>
    public IReadOnlyList<PlateOverlapRegion> Regions { get; }
    public double Area { get; }
    /// <summary>
    /// Finite world-coordinate area centroid of all shared material, after hole subtraction.
    /// This can lie outside disconnected or concave shared material. Returned by value.
    /// </summary>
    public Vector Centroid { get; }
    /// <summary>
    /// Finite world-coordinate point on the shared material, inside the pair's largest island:
    /// its pole of inaccessibility, or an interior scan-line point when a thin island defeats
    /// the bounded pole search. Markers, hit testing and inspection rings use this point.
    /// A tolerance-free tiny-clamp finding (a clamp narrower than the chord tolerance) keeps
    /// its conservative whole-clamp region, so its marker is the clamp's center, within half
    /// the clamp's size of the material. Returned by value.
    /// </summary>
    public Vector Marker { get; }
    /// <summary>A fresh world-coordinate bounds copy.</summary>
    public Box Bounds => new(bounds.X, bounds.Y, bounds.Length, bounds.Width);
}

/// <summary>
/// Two parts closer than the plate's part spacing, ordered by zero-based input index.
/// <see cref="Marker"/> is the middle of the narrowest gap, in world coordinates.
/// </summary>
public sealed record PlateSpacingPair(int PartAId, int PartBId, string PartAName, string PartBName,
    double Gap, Vector Marker);

/// <summary>
/// A part off the sheet (<see cref="OffPlate"/>) or closer to its edge than the edge spacing.
/// <see cref="Marker"/> is where it leaves the sheet or the middle of its gap to the edge.
/// </summary>
public sealed record PlateEdgeFinding(int PartId, string PartName, bool OffPlate, string Message, Vector Marker);

/// <summary>
/// One connected island of shared material in world coordinates, after hole subtraction:
/// a counterclockwise outline and its clockwise holes.
/// </summary>
public sealed class PlateOverlapRegion
{
    private static readonly IReadOnlyList<IReadOnlyList<Vector>> NoHoles = System.Array.AsReadOnly(
        System.Array.Empty<IReadOnlyList<Vector>>());

    internal PlateOverlapRegion(IEnumerable<Vector> vertices, double area,
        IEnumerable<IEnumerable<Vector>> holes = null)
    {
        Vertices = System.Array.AsReadOnly(vertices.ToArray());
        Area = area;
        Holes = holes == null
            ? NoHoles
            : System.Array.AsReadOnly(holes
                .Select(hole => (IReadOnlyList<Vector>)System.Array.AsReadOnly(hole.ToArray()))
                .ToArray());
    }

    /// <summary>Read-only counterclockwise outline with an exactly repeated closing vertex.</summary>
    public IReadOnlyList<Vector> Vertices { get; }

    /// <summary>Read-only clockwise hole rings, each with an exactly repeated closing vertex.</summary>
    public IReadOnlyList<IReadOnlyList<Vector>> Holes { get; }

    /// <summary>Positive net area: the outline's area minus its holes'.</summary>
    public double Area { get; }
}

/// <summary>An input or pair that could not be checked. IDs are zero-based input positions.</summary>
public sealed record PlateOverlapIssue(int PartAId, int? PartBId, string Message);

/// <summary>
/// Owned clean geometry and poses. Capture while inputs are stable, then analyze on a worker.
/// No live Part, Drawing, Program, or subprogram is retained.
/// </summary>
public sealed class PlateOverlapSnapshot
{
    internal PlateOverlapSnapshot(List<CapturedOverlapPart> parts, List<PlateOverlapIssue> issues,
        List<CapturedClamp> clamps = null, List<CutOffClampFinding> cutOffClampFindings = null,
        double partSpacing = 0, IReadOnlyDictionary<(int, int), double> commonLinePairs = null,
        PlateEdgeReview.SheetFrame frame = null)
    {
        CommonLinePairs = commonLinePairs ?? new Dictionary<(int, int), double>();
        Parts = parts.AsReadOnly();
        Issues = issues.AsReadOnly();
        Clamps = (clamps ?? new List<CapturedClamp>()).AsReadOnly();
        CutOffClampFindings = (cutOffClampFindings ?? new List<CutOffClampFinding>()).AsReadOnly();
        PartSpacing = partSpacing;
        Frame = frame;
    }

    internal IReadOnlyList<CapturedOverlapPart> Parts { get; }
    internal IReadOnlyList<PlateOverlapIssue> Issues { get; }

    /// <summary>Owned clamp keep-out rectangles in plate coordinates, capture order.</summary>
    internal IReadOnlyList<CapturedClamp> Clamps { get; }
    internal IReadOnlyList<CutOffClampFinding> CutOffClampFindings { get; }

    /// <summary>The plate's part spacing; zero (no spacing check) for a bare group of parts.</summary>
    internal double PartSpacing { get; }

    /// <summary>Part ids (lower first) of each pair in one common-line group, with its kerf: held to
    /// the kerf instead of <see cref="PartSpacing"/>.</summary>
    internal IReadOnlyDictionary<(int, int), double> CommonLinePairs { get; }

    /// <summary>The sheet the parts must stay on; null for a bare group of parts or an unusable sheet.</summary>
    internal PlateEdgeReview.SheetFrame Frame { get; }
}

internal sealed record CapturedOverlapPart(int Id, string Name, OverlapSource Source,
    double Rotation, Vector Location);

/// <summary>A clamp keep-out rectangle owned by the snapshot; Ordinal is its capture index.</summary>
internal sealed record CapturedClamp(int Ordinal, string Id, Box Bounds);

/// <summary>Deterministic negative PartBId for a part-vs-clamp finding's clamp slot.</summary>
internal static class ClampFindingSlot
{
    public static int IdFor(int ordinal) => -2 - ordinal;
    public static int OrdinalOf(int pairPartBId) => -2 - pairPartBId;
}
