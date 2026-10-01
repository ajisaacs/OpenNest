#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clipper2Lib;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>Per-solve, per-spacing Fill proposals. Only the members occupy material.</summary>
internal sealed class BlockCatalog : IDisposable
{
    private readonly double spacing;
    private readonly Dictionary<int, Drawing> drawings = new();
    private readonly Dictionary<int, List<Orientation>> orientations = new();
    private readonly Dictionary<int, int> attempts = new();
    internal int PreparationCount => attempts.Values.Sum();
    private readonly Dictionary<(int Type, int Quantity, double Length, double Width), IReadOnlyList<Placed>> cache = new();

    public BlockCatalog(double spacing, IReadOnlyList<PartType> types,
        IReadOnlyDictionary<int, IReadOnlyList<PairPose>> pairs)
    {
        this.spacing = spacing;
        foreach (var type in types)
        {
            var poses = type.Orientations.ToList();
            if (pairs.TryGetValue(type.Index, out var found))
                poses.AddRange(found.SelectMany(p => new[] { p.A, p.B }));
            orientations[type.Index] = poses.Distinct().ToList();
        }
    }

    internal static IReadOnlyList<Box> Rectangles(Box work, IReadOnlyList<Placed> placed, double spacing)
    {
        var free = new PathsD { new PathD
        {
            new(work.Left, work.Bottom), new(work.Right, work.Bottom),
            new(work.Right, work.Top), new(work.Left, work.Top),
        } };
        foreach (var part in placed)
        {
            // Physical occupied material, not a reference-point region for any moving part.
            // The catalog currently prepares solid outlines; future profile preparation owns holes.
            var blocked = Clipper.InflatePaths(new PathsD { part.Orientation.Outline },
                spacing + part.Orientation.Tolerance + 0.001, JoinType.Miter, EndType.Polygon,
                2, NoFitCache.Precision);
            free = Clipper.Difference(free, Clipper.TranslatePaths(blocked, part.X, part.Y),
                FillRule.NonZero, NoFitCache.Precision);
        }
        return MaximalRectangles.InRegion(free).OrderByDescending(b => b.Area())
            .ThenBy(b => b.Left).ThenBy(b => b.Bottom).ThenBy(b => b.Length).Take(2).ToArray();
    }

    public IReadOnlyList<Placed> Get(PartType type, int quantity, Box rectangle, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (quantity <= 2 || type.Orientations.Count == 0 || rectangle.Length <= 0 || rectangle.Width <= 0)
            return Array.Empty<Placed>();
        var key = (type.Index, quantity, rectangle.Length, rectangle.Width);
        if (cache.TryGetValue(key, out var cached))
            return cached;
        if (rectangle.Area() < 3 * type.Area || attempts.GetValueOrDefault(type.Index) >= 8)
            return Array.Empty<Placed>();
        attempts[type.Index] = attempts.GetValueOrDefault(type.Index) + 1;
        var result = Build(type, quantity, rectangle, token);
        cache[key] = result;
        return result;
    }

    private IReadOnlyList<Placed> Build(PartType type, int quantity, Box rectangle, CancellationToken token)
    {
        if (!drawings.TryGetValue(type.Index, out var drawing))
            drawings[type.Index] = drawing = DrawingJobMapper.CreateDrawing(type.Part);
        var plate = new Plate(new Size(rectangle.Width, rectangle.Length)) { PartSpacing = spacing };
        try
        {
            // The drawing is private: stabilize this cache entry before Fill's candidate pruning.
            var fits = BestFitCache.GetOrCompute(drawing, plate.Size.Length, plate.Size.Width, spacing);
            var sorted = fits.OrderBy(f => f.RotatedArea).ThenBy(f => f.Candidate.StrategyIndex)
                .ThenBy(f => f.Candidate.Part2Rotation).ThenBy(f => f.Candidate.Part2Offset.X)
                .ThenBy(f => f.Candidate.Part2Offset.Y).ThenBy(f => f.OptimalRotation).ToArray();
            fits.Clear();
            fits.AddRange(sorted);
            var members = PlateFillService.FillItem("Default", plate, new NestItem
            {
                Drawing = drawing,
                Quantity = quantity,
                RotationStart = type.Part.Rotation.Start,
                RotationEnd = type.Part.Rotation.End,
                StepAngle = DrawingJobMapper.LegacyStep(type.Part.Rotation),
            }, new Box(0, 0, rectangle.Length, rectangle.Width), null!, token);
            token.ThrowIfCancellationRequested();
            return Resolve(type, members.Take(quantity).ToArray(), orientations[type.Index], spacing, token);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or NotSupportedException or ArithmeticException)
        {
            return Array.Empty<Placed>();
        }
    }

    public void Dispose()
    {
        foreach (var drawing in drawings.Values)
            BestFitCache.Invalidate(drawing);
        drawings.Clear();
    }

    internal static IReadOnlyList<Placed> Resolve(PartType type, IReadOnlyList<Part> members,
        List<Orientation> orientations, double spacing, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (members.Count <= 2)
            return Array.Empty<Placed>();
        var geometry = JobPartGeometry.TryRead(type.Part.Geometry);
        if (geometry == null)
            return Array.Empty<Placed>();
        // Canonical rebinding is already performed by FillItem. Quantization removes sub-grid
        // arithmetic differences from equivalent Fill proposals; certify the resulting poses.
        var poses = members.Select(p => new NestJobPlacement(type.Part.Id, 0,
            System.Math.Round(p.Location.X, 8), System.Math.Round(p.Location.Y, 8),
            System.Math.Round(Angle.NormalizeRad(p.Rotation), 10)))
            .OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Rotation).ToArray();
        if (poses.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Rotation)
            || !type.Part.Rotation.Allows(p.Rotation)))
            return Array.Empty<Placed>();
        for (var i = 0; i < poses.Length; i++)
            for (var j = i + 1; j < poses.Length; j++)
            {
                token.ThrowIfCancellationRequested();
                if (!NestLayoutCheck.Clears(geometry, poses[i], geometry, poses[j], spacing))
                    return Array.Empty<Placed>();
            }
        var result = new List<Placed>();
        foreach (var pose in poses)
        {
            token.ThrowIfCancellationRequested();
            var orientation = orientations.FirstOrDefault(o => o.Rotation == pose.Rotation);
            if (orientation == null)
            {
                orientation = PartCatalog.CreateOrientation(type, orientations.Max(o => o.Index) + 1, pose.Rotation);
                if (orientation == null)
                    return Array.Empty<Placed>();
                orientations.Add(orientation);
            }
            result.Add(new Placed(orientation, pose.X - poses[0].X, pose.Y - poses[0].Y));
        }
        return result;
    }
}
