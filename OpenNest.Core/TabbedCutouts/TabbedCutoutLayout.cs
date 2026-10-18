using System.Collections.Generic;
using System.Linq;
using OpenNest.Breakoffs;
using OpenNest.Geometry;

namespace OpenNest.TabbedCutouts
{
    /// <summary>
    /// A <see cref="TabbedCutout"/> resolved against a drawing's entities. When <see cref="Error"/>
    /// is set nothing else is meaningful and the cutout is not applied.
    /// </summary>
    public sealed class TabbedCutoutLayout
    {
        internal TabbedCutoutLayout(string error) => Error = error;

        internal TabbedCutoutLayout() { }

        public string Error { get; }

        public bool IsValid => Error == null;

        /// <summary>
        /// The hole's contour, counterclockwise from its lowest-left vertex with its joints made
        /// exact. It is also the slug's boundary.
        /// </summary>
        public IReadOnlyList<Entity> Loop { get; internal set; }

        public double Length { get; internal set; }

        /// <summary>
        /// One open cut per span of the contour between tabs, in contour order, travelling
        /// counterclockwise (the slug on the left).
        /// </summary>
        public IReadOnlyList<IReadOnlyList<Entity>> Pieces { get; internal set; }

        /// <summary>The uncut tabs on the tab layer, each following the piece before it.</summary>
        public IReadOnlyList<IReadOnlyList<Entity>> Tabs { get; internal set; }

        /// <summary>Tab centers used, as distances along <see cref="Loop"/>.</summary>
        public IReadOnlyList<double> TabCenters { get; internal set; }

        public double TabWidth { get; internal set; }

        /// <summary>Where each piece is entered (its start) and the unit direction into the slug there.</summary>
        public IReadOnlyList<(Vector Entry, Vector SlugDirection)> Entries => Pieces
            .Select(piece =>
            {
                var tangent = BreakoffPath.TangentAt(piece, 0);
                return (BreakoffPath.Start(piece[0]), new Vector(-tangent.Y, tangent.X));
            })
            .ToList();

        /// <summary>Entities the pieces and tabs replace: the hole's original contour.</summary>
        internal IReadOnlyList<Entity> ReplacedEntities { get; set; }

        /// <summary><paramref name="count"/> evenly spaced tab centers, as the builder places them by default.</summary>
        public List<double> EvenTabCenters(int count) =>
            TabbedCutoutBuilder.EvenTabCenters(Length, count, Loop[0].Length / 2);

        /// <summary>The point at <paramref name="distance"/> along the contour, wrapping around it.</summary>
        public Vector PointAt(double distance) => BreakoffPath.PointAt(Loop, Wrap(distance));

        /// <summary>The distance along the contour closest to <paramref name="point"/>.</summary>
        public double DistanceOf(Vector point) => BreakoffPath.DistanceOf(Loop, point);

        /// <summary>
        /// <paramref name="center"/> for tab <paramref name="index"/> limited so the tab keeps a
        /// gap from its neighbours around the contour and does not pass the contour's start
        /// (which would reorder the tabs).
        /// </summary>
        public double ClampTabCenter(int index, double center)
        {
            var count = TabCenters.Count;
            if (count == 1)
                return Wrap(center);
            var spacing = TabWidth + MinGap;
            var min = index == 0 ? System.Math.Max(0, TabCenters[^1] - Length + spacing) : TabCenters[index - 1] + spacing;
            var max = index == count - 1
                ? System.Math.Min(Length - Tolerance, TabCenters[0] + Length - spacing)
                : TabCenters[index + 1] - spacing;
            return max < min ? TabCenters[index] : System.Math.Clamp(center, min, max);
        }

        internal double Wrap(double distance)
        {
            if (!(Length > 0))
                return 0;
            var wrapped = distance % Length;
            return wrapped < 0 ? wrapped + Length : wrapped;
        }

        // Wider than the builder's overlap tolerance, so a clamped drag always resolves.
        internal double MinGap => System.Math.Max(TabWidth / 2, 2 * TabbedCutoutBuilder.JoinTolerance);

        private const double Tolerance = 1e-9;
    }
}
