using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.CNC
{
    /// <summary>
    /// Turns a plate's recorded moves into a timed timeline: numbers the contours, drops
    /// etch-only contours the post would skip, sorts contours into holes and outsides, and
    /// times each move at its feed, with acceleration when a motion profile is set.
    /// </summary>
    internal static class ContourTiming
    {
        private const double ClosedTolerance = 1e-4;

        public static List<CutSimulationStep> Finish(List<CutSimulationStep> steps, Plate plate, CutParameters parameters)
        {
            NumberContours(steps);

            if (parameters.SkipScribe && RemoveScribeContours(ref steps))
                NumberContours(steps);

            ClassifyHoles(steps, plate);

            var holeFeed = parameters.HoleFeedrate is > 0 ? parameters.HoleFeedrate.Value : parameters.Feedrate;
            foreach (var step in steps)
            {
                step.NominalSpeed = step.Kind switch
                {
                    CutSimulationStepKind.Rapid => Speed(parameters.RapidTravelRate, parameters.Units),
                    CutSimulationStepKind.Cut when step.IsHole => Speed(holeFeed, parameters.Units),
                    CutSimulationStepKind.Pierce => 0,
                    _ => Speed(parameters.Feedrate, parameters.Units),
                };
                step.Duration = step.Kind == CutSimulationStepKind.Pierce
                    ? parameters.PierceTime
                    : step.NominalSpeed > 0 && step.Length > 0
                        ? TimeSpan.FromSeconds(step.Length / step.NominalSpeed)
                        : TimeSpan.Zero;
            }

            if (parameters.Motion != null)
                MotionPlanner.Plan(steps, parameters.Motion);

            var clock = TimeSpan.Zero;
            foreach (var step in steps)
            {
                step.StartTime = clock;
                clock += step.Duration;
            }
            return steps;
        }

        // Inch rates are per minute, millimeter rates per second, as Timing.CalculateTime reads them.
        private static double Speed(double rate, Units units) =>
            rate <= 0 ? 0 : units == Units.Millimeters ? rate : rate / 60;

        /// <summary>
        /// A contour starts where the torch goes down after travel: at a pierce, at a scribe
        /// after travel, on a new part, or where cutting switches to scribing or back.
        /// </summary>
        private static void NumberContours(List<CutSimulationStep> steps)
        {
            var contour = -1;
            CutSimulationStep previous = null;
            foreach (var step in steps)
            {
                if (step.Kind == CutSimulationStepKind.Rapid)
                {
                    step.ContourIndex = -1;
                }
                else
                {
                    var starts = previous == null
                        || previous.Kind == CutSimulationStepKind.Rapid
                        || step.Kind == CutSimulationStepKind.Pierce
                        || previous.PartIndex != step.PartIndex
                        || previous.Kind != CutSimulationStepKind.Pierce && previous.Kind != step.Kind;
                    if (starts)
                        contour++;
                    step.ContourIndex = contour;
                }
                previous = step;
            }
        }

        /// <summary>
        /// Leaves out contours that only scribe, as the CI Fiber post does by default, along with
        /// the travel to them; travel is then rejoined around the gap. True when any were removed.
        /// </summary>
        private static bool RemoveScribeContours(ref List<CutSimulationStep> steps)
        {
            var scribeOnly = steps
                .Where(s => s.ContourIndex >= 0)
                .GroupBy(s => s.ContourIndex)
                .Where(g => g.All(s => s.Kind == CutSimulationStepKind.Scribe))
                .Select(g => g.Key)
                .ToHashSet();
            if (scribeOnly.Count == 0)
                return false;

            var kept = new List<CutSimulationStep>();
            foreach (var step in steps)
            {
                if (!scribeOnly.Contains(step.ContourIndex))
                {
                    kept.Add(step);
                    continue;
                }
                // Drop the travel that led to the skipped contour.
                while (kept.Count > 0 && kept[^1].Kind == CutSimulationStepKind.Rapid)
                    kept.RemoveAt(kept.Count - 1);
            }

            var joined = new List<CutSimulationStep>(kept.Count);
            var at = Vector.Zero;
            foreach (var step in kept)
            {
                if (step.Kind == CutSimulationStepKind.Rapid)
                {
                    if (at.DistanceTo(step.End) > Math.Tolerance.Epsilon)
                        joined.Add(Travel(at, step.End, step.PartIndex));
                    at = step.End;
                    continue;
                }
                if (at.DistanceTo(step.Start) > Math.Tolerance.Epsilon)
                    joined.Add(Travel(at, step.Start, step.PartIndex));
                joined.Add(step);
                at = step.End;
            }

            steps = joined;
            return true;
        }

        private static CutSimulationStep Travel(Vector from, Vector to, int partIndex) =>
            new()
            {
                Kind = CutSimulationStepKind.Rapid,
                PartIndex = partIndex,
                Start = from,
                End = to,
                Length = from.DistanceTo(to),
            };

        /// <summary>
        /// Matches the CI Fiber post's interior/exterior choice (L2/L4): a cut-off, or a closed
        /// contour wound clockwise, is exterior; everything else is a hole. Lead moves are
        /// left out of the winding, as the post samples from the lead-in's end.
        /// </summary>
        private static void ClassifyHoles(List<CutSimulationStep> steps, Plate plate)
        {
            foreach (var contour in steps.Where(s => s.ContourIndex >= 0).GroupBy(s => s.ContourIndex))
            {
                var moves = contour.Where(s => s.Kind == CutSimulationStepKind.Cut).ToList();
                if (moves.Count == 0)
                    continue;

                var partIndex = moves[0].PartIndex;
                var isCutOff = partIndex < plate.Parts.Count && plate.Parts[partIndex].BaseDrawing?.IsCutOff == true;
                var isHole = !isCutOff && !IsClosedClockwise(moves);
                foreach (var step in contour)
                    step.IsHole = isHole && step.Kind != CutSimulationStepKind.Scribe;
            }
        }

        private static bool IsClosedClockwise(List<CutSimulationStep> moves)
        {
            var points = new List<Vector>();
            foreach (var move in moves.Where(m => m.Layer is not (LayerType.Leadin or LayerType.Leadout)))
            {
                if (points.Count == 0)
                    points.Add(move.Start);
                if (move.IsArc)
                {
                    var samples = System.Math.Max(1, (int)System.Math.Ceiling(System.Math.Abs(move.Sweep) / (System.Math.PI / 2)));
                    for (var i = 1; i < samples; i++)
                        points.Add(move.PointAt((double)i / samples));
                }
                points.Add(move.End);
            }

            if (points.Count < 3 || points[0].DistanceTo(points[^1]) > ClosedTolerance)
                return false;

            var area = 0.0;
            for (var i = 0; i < points.Count - 1; i++)
                area += points[i].X * points[i + 1].Y - points[i + 1].X * points[i].Y;
            return area < 0;
        }
    }
}
