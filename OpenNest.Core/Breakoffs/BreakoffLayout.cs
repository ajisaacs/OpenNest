using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Breakoffs
{
    /// <summary>
    /// A <see cref="Breakoff"/> resolved against a drawing's entities: the break line, the merged
    /// outline and the slots that cut the break line between tabs. When <see cref="Error"/> is set
    /// nothing else is meaningful and the breakoff is not applied.
    /// </summary>
    public sealed class BreakoffLayout
    {
        internal BreakoffLayout(string error) => Error = error;

        internal BreakoffLayout() { }

        public string Error { get; }

        public bool IsValid => Error == null;

        /// <summary>The outline section shared by the part and the breakoff, in travel order from its start.</summary>
        public IReadOnlyList<Entity> BreakLine { get; internal set; }

        public double Length { get; internal set; }

        /// <summary>The part outline with the break line replaced by the breakoff's edges.</summary>
        public IReadOnlyList<Entity> Outline { get; internal set; }

        /// <summary>One closed contour per cut section of the break line.</summary>
        public IReadOnlyList<IReadOnlyList<Entity>> Slots { get; internal set; }

        /// <summary>Tab centers used, as distances along <see cref="BreakLine"/>.</summary>
        public IReadOnlyList<double> TabCenters { get; internal set; }

        public double TabWidth { get; internal set; }

        /// <summary>Entities the outline and slots replace: the original outline and breakoff edges.</summary>
        internal IReadOnlyList<Entity> ReplacedEntities { get; set; }

        public Vector PointAt(double distance) => BreakoffPath.PointAt(BreakLine, distance);

        public Vector TangentAt(double distance) => BreakoffPath.TangentAt(BreakLine, distance);

        /// <summary>The distance along the break line closest to <paramref name="point"/>.</summary>
        public double DistanceOf(Vector point) => BreakoffPath.DistanceOf(BreakLine, point);

        /// <summary>
        /// <paramref name="center"/> for tab <paramref name="index"/> limited so the tab stays on the
        /// break line and keeps a gap from its neighbours.
        /// </summary>
        public double ClampTabCenter(int index, double center)
        {
            var half = TabWidth / 2;
            var min = index == 0 ? half : TabCenters[index - 1] + TabWidth + MinGap;
            var max = index == TabCenters.Count - 1 ? Length - half : TabCenters[index + 1] - TabWidth - MinGap;
            return max < min ? TabCenters[index] : System.Math.Clamp(center, min, max);
        }

        // Wider than the builder's overlap tolerance, so a clamped drag always resolves.
        internal double MinGap => System.Math.Max(TabWidth / 2, 2 * BreakoffBuilder.JoinTolerance);
    }
}
