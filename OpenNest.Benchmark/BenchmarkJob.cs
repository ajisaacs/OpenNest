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
    /// An immutable specification for one benchmark job: the full set of
    /// drawings/quantities that must be nested, and the pool of sheet sizes the
    /// engine may draw from while doing it. A single run may use several
    /// plates - possibly of different sizes - to place everything, the same
    /// way a real production job spreads across whatever plates it needs
    /// rather than being handed one fixed-size sheet.
    /// </summary>
    public class BenchmarkJob
    {
        public string SourceFile { get; init; }
        public List<Size> CandidateSizes { get; init; }
        public Spacing EdgeSpacing { get; init; }
        public double PartSpacing { get; init; }
        public int Quadrant { get; init; }
        public List<DrawingRequest> Requests { get; init; }

        public string Name => Path.GetFileNameWithoutExtension(SourceFile);

        public int TotalRequestedQuantity => Requests.Sum(r => r.Quantity);

        /// <summary>
        /// A blank plate carrying only the job's spacing/quadrant template.
        /// MultiPlateNester.CreatePlate copies these settings onto whichever
        /// size it ultimately picks; its Size is only the fallback used when
        /// nothing in the candidate pool fits, so it's set to the largest
        /// candidate rather than an arbitrary one.
        /// </summary>
        public Plate CreateTemplatePlate()
        {
            var fallbackSize = CandidateSizes
                .OrderByDescending(s => s.Width * s.Length)
                .FirstOrDefault();

            return new Plate(fallbackSize)
            {
                EdgeSpacing = EdgeSpacing,
                PartSpacing = PartSpacing,
                Quadrant = Quadrant,
            };
        }

        /// <summary>
        /// The candidate sizes as PlateOptions for MultiPlateNester.CreatePlate.
        /// Cost is area-proportional since no real per-size material pricing is
        /// available here - this only affects which size is preferred when more
        /// than one candidate fits, favoring the smaller/cheaper sheet.
        /// </summary>
        public List<PlateOption> BuildPlateOptions()
        {
            return CandidateSizes
                .Select(s => new PlateOption { Width = s.Width, Length = s.Length, Cost = s.Width * s.Length })
                .ToList();
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
