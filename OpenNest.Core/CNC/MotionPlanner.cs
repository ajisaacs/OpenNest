using System;
using System.Collections.Generic;

namespace OpenNest.CNC
{
    /// <summary>
    /// Re-times <see cref="CutSimulationStep"/>s with trapezoidal acceleration, in the style of
    /// Grbl's lookahead planner. Each cutting run (moves between a pierce and the next travel)
    /// starts and ends at rest. Corners slow to a junction-deviation speed, arcs are capped at
    /// their centripetal limit, and a backward then forward pass keeps every change of speed
    /// within the acceleration. Travel and pierces are planned on their own, from rest to rest.
    /// </summary>
    public static class MotionPlanner
    {
        private const double Straight = 0.999999;

        /// <summary>
        /// Replaces each step's <see cref="CutSimulationStep.Duration"/> with its accelerated time,
        /// from its <see cref="CutSimulationStep.NominalSpeed"/>. Pierces keep their dwell.
        /// </summary>
        public static void Plan(List<CutSimulationStep> steps, MotionProfile motion)
        {
            ArgumentNullException.ThrowIfNull(steps);
            ArgumentNullException.ThrowIfNull(motion);

            var start = 0;
            while (start < steps.Count)
            {
                var end = start + 1;
                if (IsCutting(steps[start]))
                {
                    while (end < steps.Count && IsCutting(steps[end]) && steps[end].Kind == steps[start].Kind)
                        end++;
                    PlanRun(steps, start, end, motion.CutAcceleration, motion.CornerDeviation);
                }
                else if (steps[start].Kind == CutSimulationStepKind.Rapid)
                {
                    PlanRun(steps, start, end, motion.RapidAcceleration, 0);
                }
                start = end;
            }
        }

        private static bool IsCutting(CutSimulationStep step) =>
            step.Kind is CutSimulationStepKind.Cut or CutSimulationStepKind.Scribe;

        private static void PlanRun(List<CutSimulationStep> steps, int start, int end, double acceleration, double deviation)
        {
            if (acceleration <= 0)
                return; // Keep the constant-speed timing.

            var count = end - start;
            var maxSpeed = new double[count];
            var entry = new double[count + 1]; // entry[count] is the run's exit: at rest.

            for (var i = 0; i < count; i++)
            {
                var step = steps[start + i];
                var nominal = step.NominalSpeed;
                maxSpeed[i] = step.IsArc && step.Radius > 0
                    ? System.Math.Min(nominal, System.Math.Sqrt(acceleration * step.Radius))
                    : nominal;
                entry[i] = i == 0 ? 0 : JunctionSpeed(steps[start + i - 1], step, acceleration, deviation, System.Math.Min(maxSpeed[i - 1], maxSpeed[i]));
            }

            // Backward: every step must be able to slow to the next one's entry.
            for (var i = count - 1; i >= 0; i--)
                entry[i] = System.Math.Min(entry[i], Reachable(entry[i + 1], acceleration, steps[start + i].Length));

            // Forward: and to reach that entry from its own.
            for (var i = 0; i < count; i++)
                entry[i + 1] = System.Math.Min(entry[i + 1], Reachable(entry[i], acceleration, steps[start + i].Length));

            for (var i = 0; i < count; i++)
                Profile(steps[start + i], entry[i], entry[i + 1], maxSpeed[i], acceleration);
        }

        private static double Reachable(double speed, double acceleration, double length) =>
            System.Math.Sqrt(speed * speed + 2 * acceleration * length);

        /// <summary>
        /// The fastest the head may pass from <paramref name="previous"/> into <paramref name="next"/>:
        /// v² = a·δ·sin(θ/2) / (1 − sin(θ/2)), θ being the angle the path turns through.
        /// </summary>
        internal static double JunctionSpeed(CutSimulationStep previous, CutSimulationStep next, double acceleration, double deviation, double cap)
        {
            var exit = previous.TangentAt(1);
            var enter = next.TangentAt(0);
            var cosTheta = -(exit.X * enter.X + exit.Y * enter.Y);

            if (cosTheta < -Straight)
                return cap;
            if (cosTheta > Straight || deviation <= 0)
                return 0;

            var sinHalf = System.Math.Sqrt(0.5 * (1 - cosTheta));
            var speed = System.Math.Sqrt(acceleration * deviation * sinHalf / (1 - sinHalf));
            return System.Math.Min(cap, speed);
        }

        private static void Profile(CutSimulationStep step, double entry, double exit, double maxSpeed, double acceleration)
        {
            var length = step.Length;
            if (length <= 0 || maxSpeed <= 0)
                return; // Nothing to accelerate: keep the constant-speed timing.

            var cruise = maxSpeed;
            var accelDistance = (cruise * cruise - entry * entry) / (2 * acceleration);
            var decelDistance = (cruise * cruise - exit * exit) / (2 * acceleration);
            if (accelDistance + decelDistance > length)
            {
                // Too short to reach full speed: a triangle peaking where the two ramps meet.
                cruise = System.Math.Sqrt((2 * acceleration * length + entry * entry + exit * exit) / 2);
                cruise = System.Math.Max(cruise, System.Math.Max(entry, exit));
                accelDistance = (cruise * cruise - entry * entry) / (2 * acceleration);
                decelDistance = (cruise * cruise - exit * exit) / (2 * acceleration);
            }

            var cruiseDistance = System.Math.Max(0, length - accelDistance - decelDistance);
            var seconds = (cruise - entry) / acceleration + (cruise - exit) / acceleration
                + (cruise > 0 ? cruiseDistance / cruise : 0);

            step.EntrySpeed = entry;
            step.CruiseSpeed = cruise;
            step.ExitSpeed = exit;
            step.Acceleration = acceleration;
            step.Duration = TimeSpan.FromSeconds(seconds);
        }
    }
}
