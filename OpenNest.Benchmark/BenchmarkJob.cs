using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;

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

        /// <summary>Sheet area charged per unplaced part: the largest candidate
        /// sheet. Any single part that fits the stock at all fits on one such
        /// sheet, so placing a part is never scored worse than leaving it out.</summary>
        public double UnplacedPartPenalty =>
            CandidateSizes.Count == 0 ? 0 : CandidateSizes.Max(s => s.Width * s.Length);

        /// <summary>
        /// Builds the whole-job request this job represents: one NestJobPart per
        /// requested drawing, and one NestPlateStock per candidate sheet size
        /// (unlimited quantity - the engine under test decides how many of each
        /// size it actually uses, and how demand splits across plates). The
        /// engine owns its own multi-plate/size strategy; this harness no
        /// longer picks plate sizes on the engine's behalf.
        /// </summary>
        public NestJob BuildNestJob(
            int maxPlates,
            double salvageRate = 0,
            double minimumSalvageDimension = 0
        )
        {
            var parts = Requests.Select(r =>
                DrawingJobMapper.FromDrawing(r.Drawing.Id.ToString(), r.Drawing, r.Quantity)
            );
            var stock = CandidateSizes.Select(size => new NestPlateStock(
                size.ToString(1),
                size,
                null,
                PartSpacing,
                EdgeSpacing,
                Quadrant
            ));
            return new NestJob(
                parts,
                stock,
                new NestJobOptions("Default", maxPlates, salvageRate, minimumSalvageDimension)
            );
        }
    }
}
