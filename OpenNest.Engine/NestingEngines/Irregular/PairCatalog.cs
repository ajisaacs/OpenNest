#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Math;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>
/// Two copies of one part type placed together: member A at the pair's reference point and
/// member B at <see cref="Dx"/>, <see cref="Dy"/> from it.
/// </summary>
internal sealed class PairPose
{
    public required Orientation A { get; init; }
    public required Orientation B { get; init; }
    public required double Dx { get; init; }
    public required double Dy { get; init; }

    public int TypeIndex => A.TypeIndex;

    /// <summary>Box of both members around the pair's reference point (catalog-padded).</summary>
    public double MinX => System.Math.Min(A.MinX, Dx + B.MinX);
    public double MinY => System.Math.Min(A.MinY, Dy + B.MinY);
    public double MaxX => System.Math.Max(A.MaxX, Dx + B.MaxX);
    public double MaxY => System.Math.Max(A.MaxY, Dy + B.MaxY);
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
}

/// <summary>
/// Best-fit pairs offered to the packer for part types with two or more copies.
///
/// A pair is placed through the existing free regions: its reference point must be legal for
/// member A and, shifted by the pair offset, for member B (region A intersected with region B
/// moved back by the offset). No new no-fit polygons are built. The members' clearance from
/// each other comes from the best-fit search and is re-checked here once per pair with the
/// layout check at the job spacing; a pair that fails is never offered.
/// </summary>
internal static class PairCatalog
{
    /// <summary>Best-fit results tried per type, in rank order.</summary>
    private const int CandidatesPerType = 12;

    /// <summary>Pairs kept per type; each one costs a region intersection per placement.</summary>
    private const int PairsPerType = 2;

    private const double AngleTolerance = 1e-6;

    /// <summary>
    /// Pairs for every type with quantity two or more, for one part spacing. A member rotation
    /// missing from the catalog gets a pair-only orientation (indexed after the catalog's), so
    /// both members always own cached footprints; such orientations are never offered as singles.
    /// </summary>
    /// <param name="sheetLength">Largest sheet X extent offered at this spacing.</param>
    /// <param name="sheetWidth">Largest sheet Y extent offered at this spacing.</param>
    public static IReadOnlyDictionary<int, IReadOnlyList<PairPose>> Build(
        IReadOnlyList<PartType> types,
        double spacing,
        double sheetLength,
        double sheetWidth,
        CancellationToken token
    )
    {
        var result = new Dictionary<int, IReadOnlyList<PairPose>>();
        foreach (var type in types)
        {
            token.ThrowIfCancellationRequested();
            if (type.Part.Quantity < 2 || type.Orientations.Count == 0)
                continue;
            var geometry = JobPartGeometry.TryRead(type.Part.Geometry);
            if (geometry == null)
                continue;
            var pairs = BuildForType(type, geometry, spacing, sheetLength, sheetWidth, token);
            if (pairs.Count > 0)
                result[type.Index] = pairs;
        }
        return result;
    }

    private static List<PairPose> BuildForType(
        PartType type,
        JobPartGeometry geometry,
        double spacing,
        double sheetLength,
        double sheetWidth,
        CancellationToken token
    )
    {
        var drawing = new Drawing(type.Part.Id, DrawingJobMapper.ToProgram(type.Part.Geometry));
        List<BestFitResult> fits;
        try
        {
            fits = BestFitCache.GetOrCompute(drawing, sheetLength, sheetWidth, spacing);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or NotSupportedException or ArithmeticException)
        {
            return new List<PairPose>();
        }
        finally
        {
            // The drawing exists only for this call; release its cache entry now.
            BestFitCache.Invalidate(drawing);
        }

        // Best-fit evaluates in parallel, so equal-area results arrive in any order. Sort on
        // the candidate itself as well, so the same job always picks the same pairs.
        var ranked = fits
            .Where(f => f.Keep)
            .OrderBy(f => f.RotatedArea)
            .ThenBy(f => f.Candidate.StrategyIndex)
            .ThenBy(f => f.Candidate.Part2Rotation)
            .ThenBy(f => f.Candidate.Part2Offset.X)
            .ThenBy(f => f.Candidate.Part2Offset.Y)
            .ThenBy(f => f.OptimalRotation)
            .Take(CandidatesPerType);

        var pairs = new List<PairPose>();
        var extra = new List<Orientation>();
        foreach (var fit in ranked)
        {
            token.ThrowIfCancellationRequested();
            List<Part> members;
            try
            {
                members = fit.BuildSourceParts(drawing);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                continue;
            }
            if (members.Count != 2)
                continue;
            // The whole pair may also turn a quarter: offer it along each sheet axis.
            foreach (var turn in new[] { 0.0, System.Math.PI / 2 })
            {
                var pair = Resolve(type, extra, geometry, members, turn, spacing);
                if (pair != null && !pairs.Any(p => SamePair(p, pair)))
                    pairs.Add(pair);
                if (pairs.Count >= PairsPerType)
                    return pairs;
            }
        }
        return pairs;
    }

    internal static PairPose? Resolve(
        PartType type,
        List<Orientation> extra,
        JobPartGeometry geometry,
        List<Part> members,
        double turn,
        double spacing
    )
    {
        // Turning a part about the origin turns its program and its location together,
        // so the members keep their relative placement.
        var poses = members
            .Select(member =>
            {
                var location = member.Location.Rotate(turn);
                return (Rotation: Angle.NormalizeRad(member.Rotation + turn), location.X, location.Y);
            })
            .ToArray();
        if (!type.Part.Rotation.Allows(poses[0].Rotation) || !type.Part.Rotation.Allows(poses[1].Rotation))
            return null;

        var a = new NestJobPlacement(type.Part.Id, 0, poses[0].X, poses[0].Y, poses[0].Rotation);
        var b = new NestJobPlacement(type.Part.Id, 1, poses[1].X, poses[1].Y, poses[1].Rotation);
        if (!NestLayoutCheck.Clears(geometry, a, geometry, b, spacing))
            return null;

        var oa = Find(type, extra, poses[0].Rotation);
        var ob = Find(type, extra, poses[1].Rotation);
        if (oa == null || ob == null)
            return null;
        return new PairPose { A = oa, B = ob, Dx = poses[1].X - poses[0].X, Dy = poses[1].Y - poses[0].Y };
    }

    private static Orientation? Find(PartType type, List<Orientation> extra, double rotation)
    {
        foreach (var orientation in type.Orientations.Concat(extra))
            if (SameAngle(orientation.Rotation, rotation))
                return orientation;
        var added = PartCatalog.CreateOrientation(type, type.Orientations.Count + extra.Count, rotation);
        if (added != null)
            extra.Add(added);
        return added;
    }

    private static bool SamePair(PairPose first, PairPose second) =>
        ReferenceEquals(first.A, second.A)
        && ReferenceEquals(first.B, second.B)
        && System.Math.Abs(first.Dx - second.Dx) < 1e-6
        && System.Math.Abs(first.Dy - second.Dy) < 1e-6;

    private static bool SameAngle(double first, double second)
    {
        var difference = Angle.NormalizeRad(first - second);
        return difference < AngleTolerance || Angle.TwoPI - difference < AngleTolerance;
    }
}
