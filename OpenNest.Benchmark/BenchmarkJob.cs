using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

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

        /// <summary>Source plate's machine clamp rail, resolved for each candidate size as Auto Nest does; null when none.</summary>
        public ClampRail ClampRail { get; init; }

        /// <summary>Source plate's explicit clamp zones when it has no rail, offered unchanged on every size.</summary>
        public IReadOnlyList<ClampZone> ClampZones { get; init; } = Array.Empty<ClampZone>();

        /// <summary>Saved source-nest setting; manifests have no saved salvage rate and use zero.</summary>
        public double SalvageRate { get; init; }

        /// <summary>Original hand-authored placements, if the source was a .nest with any real parts.</summary>
        public List<(Plate Plate, List<Part> Parts)> BaselinePlateRuns { get; init; }
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
        /// longer picks plate sizes on the engine's behalf. Stock comes from
        /// the same template builder as desktop Auto Nest, so clamps match it.
        /// </summary>
        public NestJob BuildNestJob(
            int maxPlates,
            double? salvageRate = null,
            double? minimumSalvageDimension = null
        )
        {
            var parts = Requests.Select(r =>
                DrawingJobMapper.FromDrawing(r.Drawing.Id.ToString(), r.Drawing, r.Quantity)
            );
            // No options would make the builder offer the template itself; an empty pool stays empty.
            var stock = CandidateSizes.Count == 0
                ? Array.Empty<NestPlateStock>()
                : NestStockBuilder.FromTemplate(
                    StockTemplate(),
                    CandidateSizes.Select(size => new PlateOption { Width = size.Width, Length = size.Length }).ToList()
                );
            return new NestJob(
                parts,
                stock,
                new NestJobOptions(
                    "Fill",
                    maxPlates,
                    salvageRate ?? SalvageRate,
                    minimumSalvageDimension ?? 0
                )
            );
        }

        /// <summary>A plate carrying this job's spacing, quadrant and clamps, sized to the largest candidate.</summary>
        private Plate StockTemplate()
        {
            var template = new Plate(CandidateSizes.MaxBy(size => size.Width * size.Length))
            {
                PartSpacing = PartSpacing,
                EdgeSpacing = EdgeSpacing,
                Quadrant = Quadrant,
            };
            if (ClampRail != null)
                template.SetClampRail(ClampRail);
            else
                foreach (var zone in ClampZones)
                    template.ClampZones.Add(zone);
            return template;
        }
    }
}
