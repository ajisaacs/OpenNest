using System;
using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.CNC
{
    public enum CutSimulationStepKind
    {
        /// <summary>Head-up travel: a rapid, a gap between programs, or an uncut (display/tab) move.</summary>
        Rapid,

        /// <summary>The dwell before a cut that starts after travel.</summary>
        Pierce,

        /// <summary>Torch-on motion through material (cut, lead-in/out, break).</summary>
        Cut,

        /// <summary>Marking motion that does not cut through.</summary>
        Scribe,
    }

    /// <summary>
    /// One timed tool motion in plate coordinates. Arcs carry a signed sweep
    /// (positive counterclockwise); lines and pierces leave it zero.
    /// </summary>
    public sealed class CutSimulationStep
    {
        public CutSimulationStepKind Kind { get; init; }
        public int PartIndex { get; init; }
        public Vector Start { get; init; }
        public Vector End { get; init; }
        public bool IsArc { get; init; }
        public Vector Center { get; init; }
        public double Radius { get; init; }
        public double StartAngle { get; init; }
        public double Sweep { get; init; }
        public double Length { get; init; }
        public TimeSpan StartTime { get; internal set; }
        public TimeSpan Duration { get; internal set; }
        public TimeSpan EndTime => StartTime + Duration;

        /// <summary>
        /// Planned speed profile in units per second: accelerate from <see cref="EntrySpeed"/>
        /// to <see cref="CruiseSpeed"/>, then decelerate to <see cref="ExitSpeed"/>. A zero
        /// <see cref="Acceleration"/> moves at constant speed for the whole step.
        /// </summary>
        public double EntrySpeed { get; internal set; }
        public double CruiseSpeed { get; internal set; }
        public double ExitSpeed { get; internal set; }
        public double Acceleration { get; internal set; }

        /// <summary>Fraction (0..1) of this step's distance covered <paramref name="elapsed"/> into it.</summary>
        public double ProgressAt(TimeSpan elapsed)
        {
            if (Duration <= TimeSpan.Zero)
                return elapsed >= TimeSpan.Zero ? 1 : 0;
            if (Acceleration <= 0 || Length <= 0)
                return System.Math.Clamp(elapsed / Duration, 0, 1);
            return System.Math.Clamp(DistanceAt(elapsed.TotalSeconds) / Length, 0, 1);
        }

        /// <summary>Head speed in units per second <paramref name="elapsed"/> into this step.</summary>
        public double SpeedAt(TimeSpan elapsed)
        {
            if (Kind == CutSimulationStepKind.Pierce || Duration <= TimeSpan.Zero)
                return 0;
            if (Acceleration <= 0)
                return Length / Duration.TotalSeconds;

            var t = System.Math.Clamp(elapsed.TotalSeconds, 0, Duration.TotalSeconds);
            var (accelEnd, cruiseEnd) = PhaseEnds();
            if (t < accelEnd)
                return EntrySpeed + Acceleration * t;
            if (t < cruiseEnd)
                return CruiseSpeed;
            return System.Math.Max(0, CruiseSpeed - Acceleration * (t - cruiseEnd));
        }

        private (double AccelEnd, double CruiseEnd) PhaseEnds()
        {
            var accelTime = (CruiseSpeed - EntrySpeed) / Acceleration;
            var accelDistance = (CruiseSpeed * CruiseSpeed - EntrySpeed * EntrySpeed) / (2 * Acceleration);
            var decelDistance = (CruiseSpeed * CruiseSpeed - ExitSpeed * ExitSpeed) / (2 * Acceleration);
            var cruise = CruiseSpeed > 0 ? System.Math.Max(0, Length - accelDistance - decelDistance) / CruiseSpeed : 0;
            return (accelTime, accelTime + cruise);
        }

        private double DistanceAt(double t)
        {
            var (accelEnd, cruiseEnd) = PhaseEnds();
            var accelDistance = EntrySpeed * accelEnd + Acceleration * accelEnd * accelEnd / 2;
            if (t < accelEnd)
                return EntrySpeed * t + Acceleration * t * t / 2;
            if (t < cruiseEnd)
                return accelDistance + CruiseSpeed * (t - accelEnd);
            var decel = t - cruiseEnd;
            return accelDistance + CruiseSpeed * (cruiseEnd - accelEnd) + CruiseSpeed * decel - Acceleration * decel * decel / 2;
        }

        /// <summary>Unit direction of travel at the start (0) or end (1) of the step.</summary>
        internal Vector TangentAt(double fraction)
        {
            if (IsArc)
            {
                var angle = StartAngle + Sweep * fraction;
                var sign = System.Math.Sign(Sweep);
                return new Vector(-System.Math.Sin(angle) * sign, System.Math.Cos(angle) * sign);
            }
            return Length > 0 ? new Vector((End.X - Start.X) / Length, (End.Y - Start.Y) / Length) : Vector.Zero;
        }

        /// <summary>The tool position after <paramref name="fraction"/> (0..1) of this step.</summary>
        public Vector PointAt(double fraction)
        {
            fraction = System.Math.Clamp(fraction, 0, 1);

            if (IsArc)
            {
                var angle = StartAngle + Sweep * fraction;
                return new Vector(
                    Center.X + Radius * System.Math.Cos(angle),
                    Center.Y + Radius * System.Math.Sin(angle));
            }

            return new Vector(
                Start.X + (End.X - Start.X) * fraction,
                Start.Y + (End.Y - Start.Y) * fraction);
        }
    }

    /// <summary>
    /// A replayable timeline of a plate's tool motion in cutting order, timed with the
    /// same feed, rapid and pierce rules as <see cref="Timing.CalculateTime(TimingInfo, CutParameters)"/>,
    /// plus <see cref="CutParameters.Motion"/> acceleration when set (see <see cref="MotionPlanner"/>).
    /// It does not model jerk, per-motion feed overrides or clamp removal.
    /// </summary>
    public sealed class CutSimulation
    {
        private readonly List<CutSimulationStep> steps;
        private readonly List<TimeSpan> contourStarts = new();
        private readonly List<TimeSpan> partStarts = new();

        private CutSimulation(List<CutSimulationStep> steps, int partCount)
        {
            this.steps = steps;
            PartCount = partCount;
            TotalTime = steps.Count == 0 ? TimeSpan.Zero : steps[^1].EndTime;

            // A contour (a cutout or a part's perimeter) begins where the torch goes down after travel.
            var lastPart = -1;
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step.Kind == CutSimulationStepKind.Rapid)
                    continue;
                var previous = i > 0 ? steps[i - 1] : null;
                if (previous is { Kind: not CutSimulationStepKind.Rapid } && previous.PartIndex == step.PartIndex)
                    continue;

                contourStarts.Add(step.StartTime);
                if (step.PartIndex != lastPart)
                {
                    partStarts.Add(step.StartTime);
                    lastPart = step.PartIndex;
                }
            }
        }

        public IReadOnlyList<CutSimulationStep> Steps => steps;
        public int PartCount { get; }
        public TimeSpan TotalTime { get; }

        /// <summary>When each contour's first cut (or its pierce) starts, in cutting order.</summary>
        public IReadOnlyList<TimeSpan> ContourStarts => contourStarts;

        /// <summary>When each part's first contour starts, in cutting order.</summary>
        public IReadOnlyList<TimeSpan> PartStarts => partStarts;

        public TimeSpan NextContour(TimeSpan time) => Next(contourStarts, time);
        public TimeSpan PreviousContour(TimeSpan time) => Previous(contourStarts, time);
        public TimeSpan NextPart(TimeSpan time) => Next(partStarts, time);
        public TimeSpan PreviousPart(TimeSpan time) => Previous(partStarts, time);

        // The first start after the playhead, or the end once none is left.
        private TimeSpan Next(List<TimeSpan> starts, TimeSpan time)
        {
            foreach (var start in starts)
            {
                if (start > time)
                    return start;
            }
            return TotalTime;
        }

        // Like a track's previous button: back to the start of the current item, or the one
        // before when the playhead is already there.
        private static TimeSpan Previous(List<TimeSpan> starts, TimeSpan time)
        {
            for (var i = starts.Count - 1; i >= 0; i--)
            {
                if (starts[i] < time)
                    return starts[i];
            }
            return TimeSpan.Zero;
        }

        public static CutSimulation Build(Plate plate, CutParameters parameters)
        {
            ArgumentNullException.ThrowIfNull(plate);
            ArgumentNullException.ThrowIfNull(parameters);

            var builder = new Builder(parameters);

            for (var i = 0; i < plate.Parts.Count; i++)
            {
                var part = plate.Parts[i];
                if (part.Program == null)
                    continue;

                builder.PartIndex = i;
                var mode = Mode.Absolute;
                var pos = part.Location;
                builder.AddProgram(part.Program, part.Location, ref mode, ref pos);
            }

            if (parameters.Motion != null)
                MotionPlanner.Plan(builder.Steps, parameters);

            return new CutSimulation(builder.Steps, plate.Parts.Count);
        }

        /// <summary>
        /// Index of the step running at <paramref name="time"/>; <see cref="Steps"/>.Count
        /// once the timeline has finished, -1 when it is empty.
        /// </summary>
        public int IndexAt(TimeSpan time)
        {
            if (steps.Count == 0)
                return -1;
            if (time >= TotalTime)
                return steps.Count;

            int lo = 0, hi = steps.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (steps[mid].StartTime <= time)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return lo;
        }

        /// <summary>
        /// Fraction (0..1) of step <paramref name="index"/> completed at <paramref name="time"/>:
        /// distance for motion, elapsed time for a pierce.
        /// </summary>
        public double FractionAt(int index, TimeSpan time)
        {
            var step = steps[index];
            if (step.Kind == CutSimulationStepKind.Pierce)
                return step.Duration <= TimeSpan.Zero
                    ? time >= step.StartTime ? 1 : 0
                    : System.Math.Clamp((time - step.StartTime) / step.Duration, 0, 1);
            return step.ProgressAt(time - step.StartTime);
        }

        public Vector ToolPositionAt(TimeSpan time)
        {
            var index = IndexAt(time);
            if (index < 0)
                return Vector.Zero;
            if (index >= steps.Count)
                return steps[^1].End;
            return steps[index].PointAt(FractionAt(index, time));
        }

        private sealed class Builder
        {
            private readonly CutParameters parameters;
            private TimeSpan clock;
            private Vector tool = Vector.Zero;
            private bool torchOn;

            public Builder(CutParameters parameters) => this.parameters = parameters;

            public List<CutSimulationStep> Steps { get; } = new();
            public int PartIndex { get; set; }

            // Sub-program offsets are relative to the calling frame's origin, as ConvertProgram reads them.
            public void AddProgram(Program program, Vector frameOrigin, ref Mode mode, ref Vector pos)
            {
                mode = program.Mode;

                for (var i = 0; i < program.Length; i++)
                {
                    switch (program[i])
                    {
                        case SubProgramCall { Program: { } sub } call:
                            var savedMode = mode;
                            var subOrigin = frameOrigin + call.Offset;
                            pos = subOrigin;
                            AddProgram(sub, subOrigin, ref mode, ref pos);
                            mode = savedMode;
                            break;

                        case RapidMove rapid:
                            var rapidEnd = Resolve(rapid.EndPoint, mode, pos, frameOrigin);
                            AddTravel(tool, rapidEnd);
                            torchOn = false; // A rapid in place still ends the cut.
                            pos = rapidEnd;
                            break;

                        case LinearMove linear:
                            var lineEnd = Resolve(linear.EndPoint, mode, pos, frameOrigin);
                            AddMotion(linear.Layer, pos, lineEnd, isArc: false, default, default);
                            pos = lineEnd;
                            break;

                        case ArcMove arc:
                            var arcEnd = Resolve(arc.EndPoint, mode, pos, frameOrigin);
                            var center = Resolve(arc.CenterPoint, mode, pos, frameOrigin);
                            AddMotion(arc.Layer, pos, arcEnd, isArc: true, center, arc.Rotation);
                            pos = arcEnd;
                            break;
                    }
                }
            }

            private static Vector Resolve(Vector point, Mode mode, Vector pos, Vector frameOrigin) =>
                mode == Mode.Incremental ? point + pos : point + frameOrigin;

            private static CutSimulationStepKind? KindOf(LayerType layer) =>
                layer switch
                {
                    LayerType.Cut or LayerType.Leadin or LayerType.Leadout or LayerType.Break => CutSimulationStepKind.Cut,
                    LayerType.Scribe => CutSimulationStepKind.Scribe,
                    _ => null, // Display and Tab motion is not emitted as cutting.
                };

            private void AddMotion(LayerType layer, Vector start, Vector end, bool isArc, Vector center, RotationType rotation)
            {
                if (KindOf(layer) is not { } kind)
                {
                    AddTravel(tool, end);
                    return;
                }

                // A program that begins cutting away from the tool reaches its start head-up.
                AddTravel(tool, start);

                if (!torchOn && kind == CutSimulationStepKind.Cut)
                {
                    Add(new CutSimulationStep
                    {
                        Kind = CutSimulationStepKind.Pierce,
                        PartIndex = PartIndex,
                        Start = start,
                        End = start,
                        Duration = parameters.PierceTime,
                    });
                }

                torchOn = kind == CutSimulationStepKind.Cut;

                if (isArc)
                    AddArc(kind, start, end, center, rotation);
                else
                    Add(new CutSimulationStep
                    {
                        Kind = kind,
                        PartIndex = PartIndex,
                        Start = start,
                        End = end,
                        Length = start.DistanceTo(end),
                        Duration = TravelTime(start.DistanceTo(end), parameters.Feedrate),
                    });
            }

            private void AddArc(CutSimulationStepKind kind, Vector start, Vector end, Vector center, RotationType rotation)
            {
                var radius = center.DistanceTo(start);
                var startAngle = center.AngleTo(start);
                var endAngle = center.AngleTo(end);
                var ccw = rotation == RotationType.CCW;

                // Coincident ends are a full circle, as ConvertProgram reads them.
                var sweep = ccw
                    ? Angle.NormalizeRad(endAngle - startAngle)
                    : -Angle.NormalizeRad(startAngle - endAngle);
                if (System.Math.Abs(sweep) < Tolerance.Epsilon)
                    sweep = ccw ? Angle.TwoPI : -Angle.TwoPI;

                var length = System.Math.Abs(sweep) * radius;
                Add(new CutSimulationStep
                {
                    Kind = kind,
                    PartIndex = PartIndex,
                    Start = start,
                    End = end,
                    IsArc = true,
                    Center = center,
                    Radius = radius,
                    StartAngle = startAngle,
                    Sweep = sweep,
                    Length = length,
                    Duration = TravelTime(length, parameters.Feedrate),
                });
            }

            private void AddTravel(Vector start, Vector end)
            {
                var distance = start.DistanceTo(end);
                if (distance <= Tolerance.Epsilon)
                {
                    tool = end;
                    return;
                }

                torchOn = false;

                Add(new CutSimulationStep
                {
                    Kind = CutSimulationStepKind.Rapid,
                    PartIndex = PartIndex,
                    Start = start,
                    End = end,
                    Length = distance,
                    Duration = TravelTime(distance, parameters.RapidTravelRate),
                });
            }

            private void Add(CutSimulationStep step)
            {
                step.StartTime = clock;
                clock += step.Duration;
                tool = step.End;
                Steps.Add(step);
            }

            // Matches Timing.CalculateTime: inch rates are per minute, millimeter rates per second.
            private TimeSpan TravelTime(double distance, double rate)
            {
                if (rate <= 0 || distance <= 0)
                    return TimeSpan.Zero;

                return parameters.Units == Units.Millimeters
                    ? TimeSpan.FromSeconds(distance / rate)
                    : TimeSpan.FromMinutes(distance / rate);
            }
        }
    }
}
