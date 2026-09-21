using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using OpenNest.Engine;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine
{
    public abstract class NestEngineBase
    {
        protected NestEngineBase(Plate plate)
        {
            Plate = plate;
        }

        public Plate Plate { get; set; }

        public int PlateNumber { get; set; }

        public NestDirection NestDirection { get; set; }

        private readonly List<PhaseResult> phaseResults = new();
        private readonly List<AngleResult> angleResults = new();

        public virtual NestPhase WinnerPhase { get; protected set; }

        public virtual List<PhaseResult> PhaseResults => phaseResults;

        public virtual List<AngleResult> AngleResults => angleResults;

        public abstract string Name { get; }

        public abstract string Description { get; }

        // --- Engine policy ---

        private IFillComparer _comparer;

        protected IFillComparer Comparer => _comparer ??= CreateComparer();

        protected virtual IFillComparer CreateComparer() => new DefaultFillComparer();

        public virtual NestDirection? PreferredDirection => null;

        public virtual ShrinkAxis TrimAxis => ShrinkAxis.Width;

        public virtual List<double> BuildAngles(
            NestItem item,
            ClassificationResult classification,
            Box workArea
        )
        {
            return new List<double>
            {
                classification.PrimaryAngle,
                classification.PrimaryAngle + OpenNest.Math.Angle.HalfPI,
            };
        }

        protected virtual void RecordProductiveAngles(List<AngleResult> angleResults) { }

        protected FillPolicy BuildPolicy() => new FillPolicy(Comparer, PreferredDirection);

        // --- Virtual methods (side-effect-free, return parts) ---

        public virtual List<Part> Fill(
            NestItem item,
            Box workArea,
            IProgress<NestProgress> progress,
            CancellationToken token
        )
        {
            return new List<Part>();
        }

        public virtual List<Part> Fill(
            List<Part> groupParts,
            Box workArea,
            IProgress<NestProgress> progress,
            CancellationToken token
        )
        {
            return new List<Part>();
        }

        public virtual List<Part> PackArea(
            Box box,
            List<NestItem> items,
            IProgress<NestProgress> progress,
            CancellationToken token
        )
        {
            return new List<Part>();
        }

        // --- Nest: compatibility façade over shared single-plate orchestration ---

        public virtual List<Part> Nest(
            List<NestItem> items,
            IProgress<NestProgress> progress,
            CancellationToken token
        )
        {
            return PlateFillOrchestrator.Nest(
                Plate,
                items,
                Comparer,
                (item, workArea, sink, cancellation) =>
                    FillExact(item, workArea, sink, cancellation),
                (workArea, packItems, sink, cancellation) =>
                    PackArea(workArea, packItems, sink, cancellation),
                progress,
                token
            );
        }

        // --- FillExact (non-virtual, delegates to virtual Fill) ---

        public List<Part> FillExact(
            NestItem item,
            Box workArea,
            IProgress<NestProgress> progress,
            CancellationToken token
        )
        {
            return Fill(item, workArea, progress, token);
        }

        // --- Convenience overloads (mutate plate, return bool) ---

        public bool Fill(NestItem item)
        {
            return Fill(item, Plate.WorkArea());
        }

        public bool Fill(NestItem item, Box workArea)
        {
            var parts = Fill(item, workArea, null, CancellationToken.None);

            if (parts == null || parts.Count == 0)
                return false;

            Plate.Parts.AddRange(parts);
            return true;
        }

        public bool Fill(List<Part> groupParts)
        {
            return Fill(groupParts, Plate.WorkArea());
        }

        public bool Fill(List<Part> groupParts, Box workArea)
        {
            var parts = Fill(groupParts, workArea, null, CancellationToken.None);

            if (parts == null || parts.Count == 0)
                return false;

            Plate.Parts.AddRange(parts);
            return true;
        }

        public bool Pack(List<NestItem> items)
        {
            var workArea = Plate.WorkArea();
            var parts = PackArea(workArea, items, null, CancellationToken.None);

            if (parts == null || parts.Count == 0)
                return false;

            Plate.Parts.AddRange(parts);
            return true;
        }

        // --- Protected utilities ---

        internal static void ReportProgress(IProgress<NestProgress> progress, ProgressReport report)
        {
            NestProgressReporter.Report(progress, report);
        }

        protected string BuildProgressSummary() =>
            PlateFillerBase.BuildProgressSummary(PhaseResults);

        protected bool IsBetterFill(List<Part> candidate, List<Part> current, Box workArea) =>
            PlateFillerBase.IsBetterFill(Comparer, candidate, current, workArea);

        protected bool IsBetterValidFill(List<Part> candidate, List<Part> current, Box workArea)
        {
            if (
                candidate != null
                && candidate.Count > 0
                && HasOverlaps(candidate, Plate.PartSpacing)
            )
            {
                Debug.WriteLine(
                    $"[IsBetterValidFill] REJECTED {candidate.Count} parts due to overlaps (current best: {current?.Count ?? 0})"
                );
                return false;
            }

            return IsBetterFill(candidate, current, workArea);
        }

        protected static bool HasOverlaps(List<Part> parts, double spacing) =>
            PlateFillerBase.HasOverlaps(parts, spacing);

    }
}
