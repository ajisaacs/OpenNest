using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Forms
{
    /// <summary>
    /// Desktop progress adapter for every whole-job engine. Building, validation and binding
    /// belong to the shared NestPipeline.
    /// </summary>
    internal static class JobEngineNest
    {
        /// <summary>
        /// Forwards job progress to a NestProgress sink. An engine's optional LegacyProgress detail
        /// (live preview parts) passes through; otherwise the stage and committed counts become a
        /// description. Committed counts are authoritative only after PlateCommitted.
        /// </summary>
        public static IProgress<NestJobProgress> CreateProgress(
            string engineName,
            IProgress<NestProgress> progress
        ) => new ProgressAdapter(engineName, progress);

        public static string Describe(string engineName, NestJobProgress value) =>
            value.Stage == NestJobStage.PlateCommitted
                ? $"{engineName}: committed plate {value.CommittedPlates} ({value.CommittedParts} parts placed)"
                : $"{engineName}: evaluating plate {WorkingPlate(value)} on stock {value.StockId}";

        /// <summary>One-based plate under evaluation; nesters that do not know it report -1.</summary>
        private static int WorkingPlate(NestJobProgress value) =>
            value.PlateIndex >= 0 ? value.PlateIndex + 1 : value.CommittedPlates + 1;

        private sealed class ProgressAdapter(string engineName, IProgress<NestProgress> progress)
            : IProgress<NestJobProgress>
        {
            public void Report(NestJobProgress value)
            {
                if (value == null)
                    return;

                if (value.LegacyProgress != null)
                {
                    progress.Report(value.LegacyProgress);
                    return;
                }

                progress.Report(
                    new NestProgress
                    {
                        Phase = NestPhase.Custom,
                        PlateNumber =
                            value.Stage == NestJobStage.PlateCommitted
                                ? value.CommittedPlates
                                : WorkingPlate(value),
                        Description = Describe(engineName, value),
                    }
                );
            }
        }
    }
}
