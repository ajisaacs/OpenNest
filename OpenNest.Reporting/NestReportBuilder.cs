using System.Collections.Immutable;
using System.Globalization;
using OpenNest.Geometry;

namespace OpenNest.Reporting;

public static class NestReportBuilder
{
    /// <summary>
    /// Capture synchronously while the caller keeps the nest stable. No quantity refresh,
    /// domain cloning, or event-wired temporary plates are used.
    /// </summary>
    public static NestReportSnapshot Capture(Nest nest, DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(nest);
        var ordered = new List<DrawingCount>();
        var byReference = new Dictionary<Drawing, DrawingCount>(ReferenceEqualityComparer.Instance);
        var plates = ImmutableArray.CreateBuilder<ReportPlate>();

        DrawingCount Register(Drawing drawing)
        {
            if (byReference.TryGetValue(drawing, out var existing))
                return existing;
            var item = new DrawingCount(drawing, "R" + (ordered.Count + 1).ToString("D3", CultureInfo.InvariantCulture));
            byReference.Add(drawing, item);
            ordered.Add(item);
            return item;
        }

        for (var index = 0; index < nest.Plates.Count; index++)
        {
            var number = index + 1;
            var plate = nest.Plates[index];
            if (plate == null)
                throw new InvalidOperationException($"Plate {number}: missing plate.");
            if (!PositiveFinite(plate.Size.Length) || !PositiveFinite(plate.Size.Width)
                || !PositiveFinite(plate.Area()))
                throw new InvalidOperationException($"Plate {number}: sheet dimensions must be finite and positive.");
            if (plate.Quantity <= 0)
                throw new InvalidOperationException($"Plate {number}: copies must be a positive integer.");
            if (!double.IsFinite(plate.PartSpacing) || plate.PartSpacing < 0)
                throw new InvalidOperationException($"Plate {number}: part spacing must be finite and nonnegative.");

            var box = plate.BoundingBox(false);
            var bounds = new ReportBounds(box.Left, box.Bottom, box.Right, box.Top);
            if (!double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Bottom)
                || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Top))
                throw new InvalidOperationException($"Plate {number}: sheet bounds are not finite.");
            var parts = ImmutableArray.CreateBuilder<ReportPlacement>();
            var cutoffs = ImmutableArray.CreateBuilder<ReportGeometry>();
            var perSheet = new Dictionary<DrawingCount, long>();
            var area = 0.0;
            for (var partIndex = 0; partIndex < plate.Parts.Count; partIndex++)
            {
                var part = plate.Parts[partIndex];
                var context = $"Plate {number}, part {partIndex + 1} ('{part?.BaseDrawing?.Name}')";
                if (part?.BaseDrawing == null)
                    throw new InvalidOperationException($"{context}: missing part or drawing reference.");
                var geometry = NestReportGeometry.Capture(part.Program, part.Location, context);
                if (part.BaseDrawing.IsCutOff)
                {
                    cutoffs.Add(geometry);
                    continue;
                }

                var item = Register(part.BaseDrawing);
                parts.Add(new ReportPlacement(item.Id, geometry));
                perSheet.TryGetValue(item, out var count);
                perSheet[item] = checked(count + 1);
                var partArea = part.BaseDrawing.Area;
                if (!double.IsFinite(partArea) || partArea < 0)
                    throw new InvalidOperationException($"{context}: net part area is invalid.");
                area += partArea;
            }

            foreach (var (item, count) in perSheet)
            {
                item.Nested = checked(item.Nested + checked(count * plate.Quantity));
                item.Plates.Add(number);
            }
            var utilization = area / plate.Area();
            if (!double.IsFinite(utilization))
                throw new InvalidOperationException($"Plate {number}: utilization is not finite.");
            plates.Add(new ReportPlate(number, bounds, plate.Quantity, plate.PartSpacing,
                utilization, parts.ToImmutable(), cutoffs.ToImmutable()));
        }

        // OrderBy is stable, so the original enumeration breaks equal name/path ties.
        foreach (var drawing in nest.Drawings
            .Where(drawing => !drawing.IsCutOff)
            .OrderBy(drawing => drawing.Name ?? "", StringComparer.Ordinal)
            .ThenBy(drawing => drawing.Source?.Path ?? "", StringComparer.Ordinal))
            Register(drawing);

        var drawings = ImmutableArray.CreateBuilder<ReportDrawing>();
        foreach (var item in ordered)
        {
            var drawing = item.Drawing;
            var context = $"Drawing {item.Id} ('{drawing.Name}')";
            var required = (long)drawing.Quantity.Required;
            if (required < 0)
                throw new InvalidOperationException($"{context}: required quantity cannot be negative.");
            var geometry = NestReportGeometry.Capture(drawing.Program, new Vector(), context);
            drawings.Add(new ReportDrawing(item.Id, drawing.Name ?? "", required, item.Nested,
                System.Math.Max(checked(required - item.Nested), 0),
                System.Math.Max(checked(item.Nested - required), 0), item.Plates.ToImmutableArray(), geometry));
        }

        var units = nest.Units switch
        {
            Units.Inches => "in",
            Units.Millimeters => "mm",
            _ => throw new InvalidOperationException("Nest: unsupported units."),
        };
        if (!double.IsFinite(nest.Thickness) || nest.Thickness < 0)
            throw new InvalidOperationException("Nest: thickness must be finite and nonnegative.");
        return new NestReportSnapshot(nest.Name ?? "", nest.Customer ?? "", nest.Notes ?? "",
            nest.Material?.Name ?? "", nest.Material?.Grade ?? "", nest.Thickness, units, generatedAt,
            drawings.ToImmutable(), plates.ToImmutable());
    }

    private static bool PositiveFinite(double value) => double.IsFinite(value) && value > 0;

    private sealed class DrawingCount(Drawing drawing, string id)
    {
        public Drawing Drawing { get; } = drawing;
        public string Id { get; } = id;
        public long Nested { get; set; }
        public List<int> Plates { get; } = [];
    }
}
