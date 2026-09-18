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
        }
    }

    internal static void ValidateCandidate(PlateCandidate candidate, IReadOnlyDictionary<string, int> remaining)
    {
        if (candidate == null) throw new InvalidOperationException("The plate nester returned a null candidate.");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var placement in candidate.Placements)
        {
            if (placement.PartId == null || !remaining.TryGetValue(placement.PartId, out var available))
                throw new InvalidOperationException("Candidate references an unknown requirement ID.");
            if (!double.IsFinite(placement.X) || !double.IsFinite(placement.Y) || !double.IsFinite(placement.Rotation))
                throw new InvalidOperationException("Candidate poses must be finite.");
            counts.TryGetValue(placement.PartId, out var count);
            if (count >= available) throw new InvalidOperationException("Candidate overproduces a requirement.");
            counts[placement.PartId] = count + 1;
        }
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
    private static bool Nonnegative(double value) => double.IsFinite(value) && value >= 0;
}
