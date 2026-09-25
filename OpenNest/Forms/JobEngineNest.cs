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
    /// Desktop adapter for whole-job engines (StockLadder and Engines/ plug-ins): builds the
    /// NestJob from auto-nest items, translates NestJobProgress for NestProgressForm, and maps
    /// result poses back onto the nest's own drawings.
    /// </summary>
    internal static class JobEngineNest
    {
        public static NestJob BuildJob(
            IReadOnlyList<NestItem> items,
            Plate template,
            List<PlateOption> plateOptions,
            double salvageRate,
            double minRemnantSize,
            int maxPlates,
            out Dictionary<string, Drawing> drawingsByPartId
        )
        {
            var parts = new List<NestJobPart>();
            drawingsByPartId = new Dictionary<string, Drawing>(StringComparer.Ordinal);

            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Quantity <= 0)
                    continue;

                var partId = $"part-{i}";
                parts.Add(DrawingJobMapper.FromItem(partId, items[i]));
                drawingsByPartId[partId] = items[i].Drawing;
            }

            var stock = new List<NestPlateStock>();

            if (plateOptions != null && plateOptions.Count > 0)
            {
                for (var i = 0; i < plateOptions.Count; i++)
                {
                    var option = plateOptions[i];
                    stock.Add(
                        new NestPlateStock(
                            $"option-{i}",
                            new Size(option.Width, option.Length),
                            quantity: null,
                            template.PartSpacing,
                            template.EdgeSpacing,
                            template.Quadrant
                        )
                    );
                }
            }
            else
            {
                stock.Add(DrawingJobMapper.FromPlate("plate", template, quantity: null));
            }

            var options = new NestJobOptions(
                maxPlates: maxPlates,
                salvageRate: plateOptions != null && plateOptions.Count > 0 ? salvageRate : 0,
                minimumSalvageDimension: minRemnantSize
            );

            return new NestJob(parts, stock, options);
        }

        /// <summary>Parts for one result sheet, bound to the caller's drawings.</summary>
        public static List<Part> CreateParts(
            NestJobPlateResult sheet,
            IReadOnlyDictionary<string, Drawing> drawingsByPartId
        )
        {
            var parts = new List<Part>(sheet.Placements.Count);

            foreach (var pose in sheet.Placements)
            {
                if (!drawingsByPartId.TryGetValue(pose.PartId, out var drawing))
                    continue;

                var part = new Part(drawing);
                part.Rotate(pose.Rotation);
                part.Location = new Vector(pose.X, pose.Y);
                part.UpdateBounds();
                parts.Add(part);
            }

            return parts;
        }

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
