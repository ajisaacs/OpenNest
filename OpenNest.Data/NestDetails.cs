using System.Linq;

namespace OpenNest.Data;

/// <summary>
/// Plate and drawing rows for one saved nest, shown below the nest list in the
/// Database-mode nest browser. The server stores only nest-level metadata, so these
/// rows are built from the nest's downloaded archive.
/// </summary>
public sealed class NestDetails
{
    public Units Units { get; init; }

    /// <summary>One row per plate, in nest order.</summary>
    public IReadOnlyList<NestPlateDetail> Plates { get; init; } = Array.Empty<NestPlateDetail>();

    /// <summary>One row per drawing (cutoffs excluded), in nest order.</summary>
    public IReadOnlyList<NestDrawingDetail> Drawings { get; init; } = Array.Empty<NestDrawingDetail>();

    /// <summary>
    /// The plates themselves, in the same order as <see cref="Plates"/>, for the plate
    /// preview. They belong to the downloaded copy of the nest, never to an open document.
    /// </summary>
    public IReadOnlyList<Plate> PlateLayouts { get; init; } = Array.Empty<Plate>();

    public static NestDetails FromNest(Nest nest)
    {
        ArgumentNullException.ThrowIfNull(nest);

        var plates = new List<NestPlateDetail>(nest.Plates.Count);
        var nested = new Dictionary<Drawing, int>(ReferenceEqualityComparer.Instance);

        for (var i = 0; i < nest.Plates.Count; i++)
        {
            var plate = nest.Plates[i];
            var parts = plate.Parts.Where(part => !part.BaseDrawing.IsCutOff).ToList();
            foreach (var part in parts)
                nested[part.BaseDrawing] = nested.GetValueOrDefault(part.BaseDrawing) + plate.Quantity;

            plates.Add(new NestPlateDetail(
                Number: i + 1,
                Duplicates: plate.Quantity,
                Width: plate.Size.Width,
                Length: plate.Size.Length,
                PartCount: parts.Count,
                DrawingCount: parts.Select(part => part.BaseDrawing).Distinct(ReferenceEqualityComparer.Instance).Count(),
                // A zero-size plate has no meaningful utilization; report 0 rather than NaN.
                Utilization: plate.Area() > 0 ? plate.Utilization() : 0));
        }

        var drawings = nest.Drawings
            .Where(drawing => !drawing.IsCutOff)
            .Select(drawing => new NestDrawingDetail(
                Name: drawing.Name ?? "",
                Customer: drawing.Customer ?? "",
                Required: drawing.Quantity.Required,
                Nested: nested.GetValueOrDefault(drawing),
                Area: drawing.Area))
            .ToList();

        return new NestDetails
        {
            Units = nest.Units,
            Plates = plates,
            Drawings = drawings,
            PlateLayouts = nest.Plates.ToList(),
        };
    }
}

/// <param name="Number">1-based position of the plate in the nest.</param>
/// <param name="Duplicates">How many times the plate is cut (<see cref="Plate.Quantity"/>).</param>
/// <param name="PartCount">Parts on one copy of the plate, cutoffs excluded.</param>
/// <param name="Utilization">Fraction of the plate area used by parts, 0 to 1.</param>
public sealed record NestPlateDetail(
    int Number,
    int Duplicates,
    double Width,
    double Length,
    int PartCount,
    int DrawingCount,
    double Utilization);

/// <param name="Nested">Parts placed across all plates, counting each plate's duplicates.</param>
public sealed record NestDrawingDetail(string Name, string Customer, int Required, int Nested, double Area)
{
    public int Remaining => System.Math.Max(0, Required - Nested);
}
