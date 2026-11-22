#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs.Placement;

/// <summary>
/// Runs the Fill strategy for one private drawing on a private plate, for callers that turn the
/// result into job poses. The caller owns the drawing and must call
/// <see cref="BestFitCache.Invalidate"/> for it when finished.
/// </summary>
internal static class PrivatePlateFill
{
    /// <param name="length">X extent of the plate and work area.</param>
    /// <param name="width">Y extent of the plate and work area.</param>
    /// <param name="quantity">Copies wanted; zero fills the whole area.</param>
    /// <param name="wholePairGrids">See <see cref="PlateFiller.WholePairGrids"/>.</param>
    /// <param name="overlapMemo">See <see cref="PlateFiller.OverlapMemo"/>: a caller filling one
    /// part again and again passes the same memo to each fill.</param>
    internal static List<Part> Run(Drawing drawing, RotationPolicy rotation, double spacing,
        double length, double width, int quantity, CancellationToken token, bool wholePairGrids = true,
        OutlineOverlapMemo overlapMemo = null)
    {
        var plate = new Plate(new Size(width, length)) { PartSpacing = spacing };
        // The drawing is private: stabilize this cache entry before Fill's candidate pruning.
        // Cancelling stops the wait; the search finishes in the background and fills the cache.
        var fits = BestFitCache.GetOrCompute(drawing, plate.Size.Length, plate.Size.Width, spacing,
            token);
        var sorted = fits.OrderBy(f => f.RotatedArea).ThenBy(f => f.Candidate.StrategyIndex)
            .ThenBy(f => f.Candidate.Part2Rotation).ThenBy(f => f.Candidate.Part2Offset.X)
            .ThenBy(f => f.Candidate.Part2Offset.Y).ThenBy(f => f.OptimalRotation).ToArray();
        fits.Clear();
        fits.AddRange(sorted);
        // A positive quantity lets supporting strategies avoid surplus rows. Zero still
        // fills the whole area (the cutout lattice caller requires an uncapped lattice).
        var filler = new PlateFiller(plate)
        {
            StopAtQuantity = true,
            WholePairGrids = wholePairGrids,
            OverlapMemo = overlapMemo,
        };
        return filler.Fill(new NestItem
        {
            Drawing = drawing,
            Quantity = quantity,
            RotationStart = rotation.Start,
            RotationEnd = rotation.End,
            StepAngle = DrawingJobMapper.LegacyStep(rotation),
        }, new Box(0, 0, length, width), null!, token);
    }
}
