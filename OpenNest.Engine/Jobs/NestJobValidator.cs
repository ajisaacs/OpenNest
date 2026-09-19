using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest;

/// <summary>Basic input and candidate accounting checks, NOT a geometry/clearance safety gate.</summary>
public static class NestJobValidator
{
    public static void Validate(NestJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        foreach (var stock in job.Plates)
        {
            var edges = stock.EdgeSpacing;
            if (!Positive(stock.Size.Width) || !Positive(stock.Size.Length) ||
                !Nonnegative(stock.PartSpacing) || !Nonnegative(edges.Left) || !Nonnegative(edges.Right) ||
                !Nonnegative(edges.Top) || !Nonnegative(edges.Bottom) || stock.Quadrant < 1 || stock.Quadrant > 4 ||
                edges.Left + edges.Right >= stock.Size.Length || edges.Top + edges.Bottom >= stock.Size.Width)
                throw new ArgumentException($"Invalid stock dimensions/settings: {stock.Id}.", nameof(job));
        }
        foreach (var part in job.Parts)
        {
            if (part.Geometry.Motions.Count == 0 || part.Geometry.Motions.Any(m =>
                !double.IsFinite(m.X) || !double.IsFinite(m.Y) ||
                !double.IsFinite(m.CenterX) || !double.IsFinite(m.CenterY)))
                throw new ArgumentException($"Geometry must contain finite motions: {part.Id}.", nameof(job));
            try
            {
                NestJobPlacementValidator.ValidateGeometry(part.Geometry);
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException($"Geometry must contain usable closed edges: {part.Id}.", nameof(job), exception);
            }
        }
    }

    internal static void ValidateCandidate(PlateCandidate candidate, NestPlateStock stock,
        IReadOnlyDictionary<string, int> remaining, IReadOnlyDictionary<string, NestJobPart> parts)
    {
        NestJobPlacementValidator.ValidateCandidate(candidate, stock, remaining, parts);
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
    private static bool Nonnegative(double value) => double.IsFinite(value) && value >= 0;
}
