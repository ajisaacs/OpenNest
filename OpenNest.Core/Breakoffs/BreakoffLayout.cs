using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Breakoffs
{
    /// <summary>
    /// A <see cref="Breakoff"/> resolved against a drawing's entities. When <see cref="Error"/> is
    /// set nothing else is meaningful and the breakoff is not applied.
    /// </summary>
    public sealed class BreakoffLayout
    {
        internal BreakoffLayout(string error) => Error = error;

        internal BreakoffLayout() { }

        public string Error { get; }

        public bool IsValid => Error == null;

        /// <summary>The joining line in travel order from its start.</summary>
        public IReadOnlyList<Entity> BreakLine { get; internal set; }

        public double Length { get; internal set; }

        /// <summary>The closed outline around part and scrap, with its joints made exact.</summary>
        public IReadOnlyList<Entity> Outline { get; internal set; }

        /// <summary>The scrap side's closed boundary (part of the outline plus the joining line).</summary>
        public IReadOnlyList<Entity> Scrap { get; internal set; }

        /// <summary>
        /// One open cut per section of the joining line between tabs, on the break layer and
        /// travel-ordered with the scrap to its left, so each is entered from the scrap.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<Entity>> Cuts { get; internal set; }

        /// <summary>Tab centers used, as distances along <see cref="BreakLine"/>.</summary>
        public IReadOnlyList<double> TabCenters { get; internal set; }

        public double TabWidth { get; internal set; }

        /// <summary>Entities the outline and cuts replace: the original outline and joining line.</summary>
        internal IReadOnlyList<Entity> ReplacedEntities { get; set; }

        public Vector PointAt(double distance) => BreakoffPath.PointAt(BreakLine, distance);

        /// <summary>The distance along the joining line closest to <paramref name="point"/>.</summary>
        public double DistanceOf(Vector point) => BreakoffPath.DistanceOf(BreakLine, point);

        /// <summary>
        /// <paramref name="center"/> for tab <paramref name="index"/> limited so the tab stays on the
        /// joining line and keeps a gap from its neighbours.
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
