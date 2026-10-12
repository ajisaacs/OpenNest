using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Engine.Jobs;

/// <summary>Basic input and candidate accounting checks, NOT a geometry/clearance safety gate.</summary>
public static class NestJobValidator
{
    public static void Validate(NestJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        ValidateStockCosts(job.Plates);
        foreach (var stock in job.Plates)
        {
            var edges = stock.EdgeSpacing;
            if (
                !Positive(stock.Area)
                || !Positive(stock.Size.Width)
                || !Positive(stock.Size.Length)
                || !Nonnegative(stock.PartSpacing)
                || !Nonnegative(edges.Left)
                || !Nonnegative(edges.Right)
                || !Nonnegative(edges.Top)
                || !Nonnegative(edges.Bottom)
                || stock.Quadrant < 1
                || stock.Quadrant > 4
                || edges.Left + edges.Right >= stock.Size.Length
                || edges.Top + edges.Bottom >= stock.Size.Width
            )
                throw new ArgumentException(
                    $"Invalid stock dimensions/settings: {stock.Id}.",
                    nameof(job)
                );
            ValidateClampZones(stock, job);
        }
        foreach (var part in job.Parts)
        {
            if (
                part.Geometry.Motions.Count == 0
                || part.Geometry.Motions.Any(m =>
                    !double.IsFinite(m.X)
                    || !double.IsFinite(m.Y)
                    || !double.IsFinite(m.CenterX)
                    || !double.IsFinite(m.CenterY)
                )
            )
                throw new ArgumentException(
                    $"Geometry must contain finite motions: {part.Id}.",
                    nameof(job)
                );
            try
            {
                _ = JobPartGeometry.Read(part.Geometry);
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException(
                    $"Geometry must contain usable closed edges: {part.Id}. {exception.Message}",
                    nameof(job),
                    exception
                );
            }
        }
    }

    public static void ValidateStockCosts(IEnumerable<NestPlateStock> stocks)
    {
        var available = stocks.Where(s => s.Quantity != 0).ToArray();
        if (available.Any(s => s.Cost.HasValue) && available.Any(s => !s.Cost.HasValue))
            throw new ArgumentException("Specify a positive cost for every available stock row or omit all costs. Missing: "
                + string.Join(", ", available.Where(s => !s.Cost.HasValue).Select(s => s.Id)));
    }

    /// <summary>
    /// Every stock zone must be uniquely identified and lie within its own sheet
    /// (edge-inclusive; geometry and finiteness already enforced by <see cref="ClampZone"/>).
    /// Zones are compared in the placement frame, where the stock carries them.
    /// </summary>
    private static void ValidateClampZones(NestPlateStock stock, NestJob job)
    {
        if (stock.ClampZones.Count == 0)
            return;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var left = stock.Quadrant is 1 or 4 ? 0 : -stock.Size.Length;
        var bottom = stock.Quadrant is 1 or 2 ? 0 : -stock.Size.Width;
        const double slack = NestTolerances.WorkAreaSlack;

        foreach (var zone in stock.ClampZones)
        {
            if (!ids.Add(zone.Id))
                throw new ArgumentException(
                    $"Clamp zone id '{zone.Id}' is defined twice on stock {stock.Id}.",
                    nameof(job)
                );

            var b = zone.Bounds;
            if (
                b.Left < left - slack
                || b.Right > left + stock.Size.Length + slack
                || b.Bottom < bottom - slack
                || b.Top > bottom + stock.Size.Width + slack
            )
                throw new ArgumentException(
                    $"Clamp zone '{zone.Id}' on stock {stock.Id} lies outside the sheet.",
                    nameof(job)
                );
        }
    }

    internal static void ValidateCandidate(
        PlateCandidate candidate,
        NestPlateStock stock,
        IReadOnlyDictionary<string, int> remaining,
        IReadOnlyDictionary<string, NestJobPart> parts
    )
    {
        NestJobPlacementValidator.ValidateCandidate(candidate, stock, remaining, parts);
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;

    private static bool Nonnegative(double value) => double.IsFinite(value) && value >= 0;
}
