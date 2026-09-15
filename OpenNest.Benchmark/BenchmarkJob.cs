using OpenNest.Geometry;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenNest.Benchmark
{
    /// <summary>
    /// One request to nest a specific drawing, with the quantity and rotation
    /// constraints pulled from its source .nest file.
    /// </summary>
    public class DrawingRequest
    {
        public Drawing Drawing { get; init; }
        public int Quantity { get; init; }
        public int Priority { get; init; }
        public double StepAngle { get; init; }
        public double RotationStart { get; init; }
        public double RotationEnd { get; init; }
    }

    /// <summary>
    /// An immutable specification for one benchmark job: a set of drawings/quantities
    /// to be nested onto a plate of a given size. Every engine under test gets a fresh
    /// Plate and NestItem list built from this spec via CreatePlate()/CreateItems(),
    /// so one engine's run can never leak mutated state into another's.
    /// </summary>
    public class BenchmarkJob
    {
        public string SourceFile { get; init; }
        public string SheetSizeLabel { get; init; }
        public Size PlateSize { get; init; }
        public Spacing EdgeSpacing { get; init; }
        public double PartSpacing { get; init; }
        public int Quadrant { get; init; }
        public List<DrawingRequest> Requests { get; init; }

        public string Name => $"{Path.GetFileNameWithoutExtension(SourceFile)} [{SheetSizeLabel}]";

        public int TotalRequestedQuantity => Requests.Sum(r => r.Quantity);

        public Plate CreatePlate()
        {
            return new Plate(PlateSize)
            {
                EdgeSpacing = EdgeSpacing,
                PartSpacing = PartSpacing,
                Quadrant = Quadrant,
            };
        }

        public List<NestItem> CreateItems()
        {
            return Requests.Select(r => new NestItem
            {
                Drawing = r.Drawing,
                Quantity = r.Quantity,
                Priority = r.Priority,
                StepAngle = r.StepAngle,
                RotationStart = r.RotationStart,
                RotationEnd = r.RotationEnd,
            }).ToList();
        }
    }
}
