using System.Collections.Immutable;
using System.Globalization;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Reporting;

public static class NestReportBuilder
{
    /// <summary>
    /// Capture synchronously while the caller keeps the nest stable. No quantity refresh,
    /// domain cloning, or event-wired temporary plates are used. With <paramref name="cutParameters"/>,
    /// each plate also gets an estimated cut time from <see cref="CutSimulation"/>.
    /// </summary>
    public static NestReportSnapshot Capture(Nest nest, DateTimeOffset generatedAt, CutParameters? cutParameters = null)
    {
        ArgumentNullException.ThrowIfNull(nest);
        var registry = new DrawingRegistry();
        var plates = ImmutableArray.CreateBuilder<ReportPlate>();

        var plateCount = ReportedPlateCount(nest);
        for (var index = 0; index < plateCount; index++)
            plates.Add(CapturePlate(nest.Plates[index], index + 1, registry, cutParameters));

        // OrderBy is stable, so the original enumeration breaks equal name/path ties.
        foreach (var drawing in nest.Drawings
            .Where(drawing => !drawing.IsCutOff)
            .OrderBy(drawing => drawing.Name ?? "", StringComparer.Ordinal)
            .ThenBy(drawing => drawing.Source?.Path ?? "", StringComparer.Ordinal))
            registry.Register(drawing);

        var drawings = ImmutableArray.CreateBuilder<ReportDrawing>();
        foreach (var item in registry.Ordered)
            drawings.Add(CaptureDrawing(item));

        var units = UnitsLabel(nest.Units);
        if (!double.IsFinite(nest.Thickness) || nest.Thickness < 0)
            throw new InvalidOperationException("Nest: thickness must be finite and nonnegative.");
        return new NestReportSnapshot(nest.Name ?? "", nest.Customer ?? "", nest.Notes ?? "",
            nest.Material?.Name ?? "", nest.Material?.Grade ?? "", nest.Thickness, units, generatedAt,
            drawings.ToImmutable(), plates.ToImmutable())
        {
            HasCutTimes = cutParameters != null,
        };
    }

    /// <summary>
    /// The editor can leave unused stock at the end of a job. Exclude those placeholders
    /// from report pages and totals without changing plate numbers or the live nest.
    /// </summary>
    private static int ReportedPlateCount(Nest nest)
    {
        var plateCount = nest.Plates.Count;
        while (plateCount > 0 && nest.Plates[plateCount - 1]?.Parts.Count == 0)
            plateCount--;
        return plateCount;
    }

    private static ReportPlate CapturePlate(Plate plate, int number, DrawingRegistry registry, CutParameters? cutParameters)
    {
        ValidatePlate(plate, number);
        var bounds = CaptureBounds(plate, number);
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

            var item = registry.Register(part.BaseDrawing);
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
        return new ReportPlate(number, bounds, plate.Quantity, plate.PartSpacing,
            utilization, parts.ToImmutable(), cutoffs.ToImmutable())
        {
            CutTime = cutParameters == null ? null : EstimateCutTime(plate, cutParameters, number),
        };
    }

    private static void ValidatePlate(Plate plate, int number)
    {
        if (plate == null)
            throw new InvalidOperationException($"Plate {number}: missing plate.");
        if (!PositiveFinite(plate.Size.Length) || !PositiveFinite(plate.Size.Width)
            || !PositiveFinite(plate.Area()))
            throw new InvalidOperationException($"Plate {number}: sheet dimensions must be finite and positive.");
        if (plate.Quantity <= 0)
            throw new InvalidOperationException($"Plate {number}: copies must be a positive integer.");
        if (!double.IsFinite(plate.PartSpacing) || plate.PartSpacing < 0)
            throw new InvalidOperationException($"Plate {number}: part spacing must be finite and nonnegative.");
    }

    private static ReportBounds CaptureBounds(Plate plate, int number)
    {
        var box = plate.BoundingBox(false);
        var bounds = new ReportBounds(box.Left, box.Bottom, box.Right, box.Top);
        if (!double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Bottom)
            || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Top))
            throw new InvalidOperationException($"Plate {number}: sheet bounds are not finite.");
        return bounds;
    }

    private static ReportDrawing CaptureDrawing(DrawingCount item)
    {
        var drawing = item.Drawing;
        var context = $"Drawing {item.Id} ('{drawing.Name}')";
        var required = (long)drawing.Quantity.Required;
        if (required < 0)
            throw new InvalidOperationException($"{context}: required quantity cannot be negative.");
        var geometry = NestReportGeometry.Capture(drawing.Program, new Vector(), context);
        return new ReportDrawing(item.Id, drawing.Name ?? "", required, item.Nested,
            System.Math.Max(checked(required - item.Nested), 0),
            System.Math.Max(checked(item.Nested - required), 0), item.Plates.ToImmutableArray(), geometry);
    }

    private static string UnitsLabel(Units units) => units switch
    {
        Units.Inches => "in",
        Units.Millimeters => "mm",
        _ => throw new InvalidOperationException("Nest: unsupported units."),
    };

    /// <summary>One copy of the plate: its cutting timeline plus clamp removal, as Calculate Cut Time.</summary>
    private static TimeSpan EstimateCutTime(Plate plate, CutParameters parameters, int number)
    {
        var time = CutSimulation.Build(plate, parameters).TotalTime + TimeSpan.FromMinutes(plate.ClampRemovalMinutes);
        if (time < TimeSpan.Zero)
            throw new InvalidOperationException($"Plate {number}: estimated cut time is invalid.");
        return time;
    }

    private static bool PositiveFinite(double value) => double.IsFinite(value) && value > 0;

    /// <summary>Assigns report IDs in first-seen order; identity is by reference, not name.</summary>
    private sealed class DrawingRegistry
    {
        private readonly Dictionary<Drawing, DrawingCount> byReference = new(ReferenceEqualityComparer.Instance);

        public List<DrawingCount> Ordered { get; } = [];

        public DrawingCount Register(Drawing drawing)
        {
            if (byReference.TryGetValue(drawing, out var existing))
                return existing;
            var item = new DrawingCount(drawing, (Ordered.Count + 1).ToString(CultureInfo.InvariantCulture));
            byReference.Add(drawing, item);
            Ordered.Add(item);
            return item;
        }
    }

    private sealed class DrawingCount(Drawing drawing, string id)
    {
        public Drawing Drawing { get; } = drawing;
        public string Id { get; } = id;
        public long Nested { get; set; }
        public List<int> Plates { get; } = [];
    }
}
