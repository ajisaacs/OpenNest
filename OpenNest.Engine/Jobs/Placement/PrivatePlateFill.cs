#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs.Placement;

/// <summary>
/// Runs the Default Fill for one private drawing on a private plate, for callers that turn the
/// result into job poses. The caller owns the drawing and must call
/// <see cref="BestFitCache.Invalidate"/> for it when finished.
/// </summary>
internal static class PrivatePlateFill
{
    /// <param name="length">X extent of the plate and work area.</param>
    /// <param name="width">Y extent of the plate and work area.</param>
    /// <param name="quantity">Copies wanted; zero fills the whole area.</param>
    internal static List<Part> Run(Drawing drawing, RotationPolicy rotation, double spacing,
        double length, double width, int quantity, CancellationToken token)
    {
        var plate = new Plate(new Size(width, length)) { PartSpacing = spacing };
        // The drawing is private: stabilize this cache entry before Fill's candidate pruning.
        var fits = BestFitCache.GetOrCompute(drawing, plate.Size.Length, plate.Size.Width, spacing);
        var sorted = fits.OrderBy(f => f.RotatedArea).ThenBy(f => f.Candidate.StrategyIndex)
            .ThenBy(f => f.Candidate.Part2Rotation).ThenBy(f => f.Candidate.Part2Offset.X)
            .ThenBy(f => f.Candidate.Part2Offset.Y).ThenBy(f => f.OptimalRotation).ToArray();
        fits.Clear();
        fits.AddRange(sorted);
        return PlateFillService.FillItem("Default", plate, new NestItem
        {
            Drawing = drawing,
            Quantity = quantity,
            RotationStart = rotation.Start,
            RotationEnd = rotation.End,
            StepAngle = DrawingJobMapper.LegacyStep(rotation),
        }, new Box(0, 0, length, width), null!, token);
    }
}
