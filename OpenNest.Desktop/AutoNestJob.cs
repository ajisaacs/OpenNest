using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;

namespace OpenNest.Desktop
{
    /// <summary>
    /// Auto Nest with the desktop dialog's defaults and no dialog: every drawing's remaining
    /// quantity, with its priority and rotation constraints, on sheets like a template plate,
    /// through <see cref="NestPipeline"/>. Only a valid, keepable result that places something
    /// is committed, onto the nest's empty plates first and then new plates from its defaults.
    /// </summary>
    public static class AutoNestJob
    {
        /// <summary>
        /// The job for <paramref name="nest"/>'s remaining quantities on stock like
        /// <paramref name="template"/>; null when nothing remains. The nest is not changed, so
        /// the caller can solve it off its thread.
        /// </summary>
        public static NestPipelineRequest ForRemaining(Nest nest, Plate template, string engineName)
        {
            ArgumentNullException.ThrowIfNull(nest);
            ArgumentNullException.ThrowIfNull(template);
            var items = nest.Drawings
                .Where(d => !d.IsCutOff && d.Quantity.Remaining > 0)
                .Select(d => new NestItem
                {
                    Drawing = d,
                    Quantity = d.Quantity.Remaining,
                    Priority = d.Priority,
                    RotationStart = d.Constraints.StartAngle,
                    RotationEnd = d.Constraints.EndAngle,
                    StepAngle = d.Constraints.StepAngle,
                })
                .ToList();
            if (items.Count == 0)
                return null;

            return new NestPipelineRequest(engineName, items,
                NestStockBuilder.FromTemplate(template, null),
                new NestJobOptions(maxPlates: NestRenest.MaxPlates, salvageRate: 0,
                    minimumSalvageDimension: nest.MinimumRemnantDimension));
        }

        /// <summary>
        /// Commits <paramref name="result"/> to <paramref name="nest"/> on the nest's own thread
        /// when it is valid, keepable and places something; otherwise leaves the nest unchanged
        /// and says why in the note.
        /// </summary>
        public static NestRenestResult Keep(Nest nest, NestPipelineResult result)
        {
            ArgumentNullException.ThrowIfNull(nest);
            return NestRenest.Keep(result,
                valid => NestPipelineCommit.ApplyToPlates(valid, () => EmptyOrNewPlate(nest)));
        }

        private static Plate EmptyOrNewPlate(Nest nest) =>
            nest.Plates.FirstOrDefault(p => p.Parts.Count == 0) ?? nest.CreatePlate();

        /// <summary>
        /// Why <see cref="Keep"/> would commit nothing for <paramref name="result"/>, naming the
        /// first problem; null when it would commit.
        /// </summary>
        public static string RefusalReason(NestPipelineResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (!result.IsValid)
                return $"Auto Nest found {result.Violations.Count} layout problem"
                    + $"{(result.Violations.Count == 1 ? "" : "s")} (first: {result.Violations[0]}); nothing was changed.";
            if (!result.CanKeep)
                return "Auto Nest's layout cannot be kept faithfully; nothing was changed.";
            if (result.Plates.All(p => p.Parts.Count == 0))
                return "Auto Nest placed nothing; nothing was changed."
                    + (result.Unplaced.Count > 0
                        ? " Not placed: " + string.Join("; ", result.Unplaced.Select(u => u.Describe())) + "."
                        : "");
            return null;
        }
    }
}
