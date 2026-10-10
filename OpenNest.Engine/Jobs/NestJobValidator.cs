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
