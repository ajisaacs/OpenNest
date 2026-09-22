using System;
using System.Collections.Generic;
using System.Threading;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Geometry;

namespace OpenNest.Engine.Strategies
{
    public class FillContext
    {
        public NestItem Item { get; init; }
        public Box WorkArea { get; init; }
        public Plate Plate { get; init; }
        public int PlateNumber { get; init; }
        public CancellationToken Token { get; init; }
        public IProgress<NestProgress> Progress { get; init; }
        public FillPolicy Policy { get; init; } = new FillPolicy(new DefaultFillComparer());
        public int MaxQuantity { get; init; }
        public PartType PartType { get; set; }

        /// <summary>
        /// The pre-canonicalization drawing that <see cref="Item"/>'s canonical copy was built
        /// from. When set, <see cref="ReportProgress"/> rebinds reported parts to this drawing
        /// before they reach <see cref="Progress"/>, so intermediate previews (e.g. the Nesting
        /// Progress dialog, PlateView's active-parts overlay) show the drawing's real/visible
        /// orientation instead of the transient canonical (MBR-axis-aligned) one. Null when the
        /// caller isn't operating in canonical frame (e.g. tests driving a strategy directly).
        /// </summary>
        public Drawing OriginalDrawing { get; init; }

        public List<Part> CurrentBest { get; set; }

        /// <summary>For progress reporting only; comparisons use Policy.Comparer.</summary>
        public FillScore CurrentBestScore { get; set; }
        public NestPhase WinnerPhase { get; set; }
        public NestPhase ActivePhase { get; set; }
        public List<PhaseResult> PhaseResults { get; } = new();
        public List<AngleResult> AngleResults { get; } = new();

        public Dictionary<string, object> SharedState { get; } = new();

        /// <summary>
        /// Standard progress reporting for strategies and fillers. Reports intermediate
        /// results using the current ActivePhase, PlateNumber, and WorkArea.
        /// When the reported parts beat the current pipeline best, promotes the
        /// result to IsOverallBest so the UI updates immediately.
        /// </summary>
        public void ReportProgress(List<Part> parts, string description)
        {
            var isNewBest =
                parts != null
                && parts.Count > 0
                && Policy.Comparer.IsBetter(parts, CurrentBest, WorkArea);

            if (isNewBest)
            {
                CurrentBest = parts;
                CurrentBestScore = FillScore.Compute(parts, WorkArea);
                WinnerPhase = ActivePhase;
            }

            NestProgressReporter.Report(
                Progress,
                new ProgressReport
                {
                    Phase = ActivePhase,
                    PlateNumber = PlateNumber,
                    Parts = ToOriginalFrame(isNewBest ? parts : CurrentBest),
                    WorkArea = WorkArea,
                    Description = description,
                    IsOverallBest = isNewBest,
                }
            );
        }

        /// <summary>
        /// Rebinds <paramref name="parts"/> to <see cref="OriginalDrawing"/> for outward-facing
        /// progress reports. Uses a shallow list copy (not per-part <see cref="Part.Clone"/>) so
        /// <see cref="CanonicalFrame.RebindToOriginal"/>'s in-place slot replacement can't corrupt
        /// the caller's list (e.g. <see cref="CurrentBest"/>) — <c>Part.Clone()</c> re-derives its
        /// target rotation from <c>BaseDrawing.Program.Rotation + Rotation</c>, which double-counts
        /// the canonical drawing's baked source angle whenever it's non-zero. No-op when
        /// <see cref="OriginalDrawing"/> isn't set. Internal so callers that build their own
        /// <see cref="ProgressReport"/> outside <see cref="ReportProgress"/> (e.g. the fallback
        /// report in <c>DefaultPlateFiller.RunPipeline</c> for strategies that don't self-report)
        /// can apply the same rebind before reaching the UI.
        /// </summary>
        internal List<Part> ToOriginalFrame(List<Part> parts)
        {
            if (parts == null || parts.Count == 0 || OriginalDrawing == null)
                return parts;

            return CanonicalFrame.RebindToOriginal(new List<Part>(parts), OriginalDrawing);
        }
    }
}
