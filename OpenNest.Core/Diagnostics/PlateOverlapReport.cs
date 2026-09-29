using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.Diagnostics;

/// <summary>An owned diagnostic result. An empty Pairs list is clear only if IsComplete is true.</summary>
public sealed class PlateOverlapReport
{
    internal PlateOverlapReport(List<PlateOverlapPair> pairs, List<PlateOverlapIssue> issues)
    {
        Pairs = pairs.AsReadOnly();
        Issues = issues.AsReadOnly();
    }

    public IReadOnlyList<PlateOverlapPair> Pairs { get; }
    public IReadOnlyList<PlateOverlapIssue> Issues { get; }
    public bool IsComplete => Issues.Count == 0;
    public double ChordTolerance => PlateOverlapAnalyzer.ChordTolerance;
}

/// <summary>Shared material for two input positions, ordered by zero-based input index.</summary>
public sealed class PlateOverlapPair
{
    private readonly Box bounds;

    internal PlateOverlapPair(int partAId, int partBId, string partAName, string partBName,
        List<PlateOverlapRegion> regions, Vector centroid)
    {
        PartAId = partAId;
        PartBId = partBId;
        PartAName = partAName;
        PartBName = partBName;
        Regions = regions.AsReadOnly();
        Area = regions.Sum(region => region.Area);
        Centroid = centroid;
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
    /// <summary>Convex fragments, not connected islands; no mutable kernel polygons are exposed.</summary>
    public IReadOnlyList<PlateOverlapRegion> Regions { get; }
    public double Area { get; }
    /// <summary>
    /// Finite world-coordinate area centroid of all shared material, after hole subtraction.
    /// This can lie outside disconnected or concave shared material. Returned by value.
    /// </summary>
    public Vector Centroid { get; }
    /// <summary>A fresh world-coordinate bounds copy.</summary>
    public Box Bounds => new(bounds.X, bounds.Y, bounds.Length, bounds.Width);
}

/// <summary>A positive-area, hole-subtracted convex polygon in world coordinates.</summary>
public sealed class PlateOverlapRegion
{
    internal PlateOverlapRegion(IEnumerable<Vector> vertices, double area)
    {
        Vertices = System.Array.AsReadOnly(vertices.ToArray());
        Area = area;
    }

    /// <summary>Read-only vertices with an exactly repeated closing vertex.</summary>
    public IReadOnlyList<Vector> Vertices { get; }
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
    internal PlateOverlapSnapshot(List<CapturedOverlapPart> parts, List<PlateOverlapIssue> issues)
    {
        Parts = parts.AsReadOnly();
        Issues = issues.AsReadOnly();
    }

    internal IReadOnlyList<CapturedOverlapPart> Parts { get; }
    internal IReadOnlyList<PlateOverlapIssue> Issues { get; }
}

internal sealed record CapturedOverlapPart(int Id, string Name, List<Entity> Entities,
    double Rotation, Vector Location);
