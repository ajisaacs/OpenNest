using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Threading;

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
            var isNewBest = parts != null && parts.Count > 0
                && Policy.Comparer.IsBetter(parts, CurrentBest, WorkArea);

            if (isNewBest)
            {
                CurrentBest = parts;
                CurrentBestScore = FillScore.Compute(parts, WorkArea);
                WinnerPhase = ActivePhase;
            }

            NestEngineBase.ReportProgress(Progress, new ProgressReport
            {
                Phase = ActivePhase,
                PlateNumber = PlateNumber,
                Parts = isNewBest ? parts : CurrentBest,
                WorkArea = WorkArea,
                Description = description,
                IsOverallBest = isNewBest,
            });
        }
    }
}
