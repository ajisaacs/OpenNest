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

        public NestPhase WinnerPhase { get; protected set; }

        public List<PhaseResult> PhaseResults { get; } = new();

        public List<AngleResult> AngleResults { get; } = new();

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

        protected string BuildProgressSummary()
        {
            if (PhaseResults.Count == 0)
                return null;

            var parts = new List<string>(PhaseResults.Count);

            foreach (var r in PhaseResults)
                parts.Add($"{r.Phase.ShortName()}: {r.PartCount}");

            return string.Join(" | ", parts);
        }

        protected bool IsBetterFill(List<Part> candidate, List<Part> current, Box workArea) =>
            Comparer.IsBetter(candidate, current, workArea);

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

        protected static bool HasOverlaps(List<Part> parts, double spacing)
        {
            if (parts == null || parts.Count <= 1)
                return false;

            for (var i = 0; i < parts.Count; i++)
            {
                var box1 = parts[i].BoundingBox;

                for (var j = i + 1; j < parts.Count; j++)
                {
                    var box2 = parts[j].BoundingBox;

                    var overlapX =
                        System.Math.Min(box1.Right, box2.Right)
                        - System.Math.Max(box1.Left, box2.Left);
                    var overlapY =
                        System.Math.Min(box1.Top, box2.Top)
                        - System.Math.Max(box1.Bottom, box2.Bottom);

                    if (overlapX <= Tolerance.Epsilon || overlapY <= Tolerance.Epsilon)
                        continue;

                    List<Vector> pts;

                    if (parts[i].Intersects(parts[j], out pts))
                    {
                        var b1 = parts[i].BoundingBox;
                        var b2 = parts[j].BoundingBox;
                        Debug.WriteLine(
                            $"[HasOverlaps] Overlap: part[{i}] ({parts[i].BaseDrawing?.Name}) @ ({b1.Left:F2},{b1.Bottom:F2})-({b1.Right:F2},{b1.Top:F2}) rot={parts[i].Rotation:F2}"
                                + $" vs part[{j}] ({parts[j].BaseDrawing?.Name}) @ ({b2.Left:F2},{b2.Bottom:F2})-({b2.Right:F2},{b2.Top:F2}) rot={parts[j].Rotation:F2}"
                                + $" intersections={pts?.Count ?? 0}"
                        );
                        return true;
                    }
                }
            }

            return false;
        }

    }
}
