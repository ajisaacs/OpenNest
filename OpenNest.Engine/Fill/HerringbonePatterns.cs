using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Builds herringbone pairs: a part turned so one of its convex-hull edges lies along a sheet
    /// axis, and a copy turned 180° more, nested into the row the first part makes when repeated
    /// along that axis. Tiled, these give rows of same-facing parts nested into each other with
    /// every other row reversed. Best-fit pairs rank by pair bounding box, so a pair whose parts
    /// touch only at their ends (as an L bracket's legs do in a herringbone) ranks far too low to
    /// be tried, and the pair's own hull rarely holds the angle its rows need.
    /// </summary>
    internal static class HerringbonePatterns
    {
        /// <summary>Hull angles tried, longest edges first.</summary>
        private const int MaxHullAngles = 8;

        /// <summary>Offsets along the row, across one row pitch, at which the reversed part is tried.</summary>
        private const int ShiftSamples = 32;

        /// <summary>Finer offsets tried either side of each kept sample.</summary>
        private const int RefineSamples = 8;

        /// <summary>Pairs kept per rotation and row axis: those making the shallowest double row.</summary>
        private const int KeptShifts = 2;

        private static readonly double AngleTolerance = Angle.ToRadians(1);

        /// <summary>One herringbone pair of a row: where the reversed part starts beside the row
        /// and how far it slides back onto it.</summary>
        private readonly record struct Shift(Vector Offset, Vector Slide);

        /// <summary>The pairs kept for one part rotation and row axis.</summary>
        private sealed record Row(double Rotation, NestDirection Axis, Shift[] Shifts);

        /// <summary>
        /// Every pair of a drawing at one spacing, grouped by part rotation: what the search
        /// found, without the parts. It depends on the drawing's geometry and the spacing alone,
        /// so it is kept with the drawing's best fits and each fill only rebuilds the parts.
        /// </summary>
        private sealed record Recipe(List<List<Row>> Groups);

        /// <summary>
        /// The patterns to tile, grouped by part rotation, each group with its row axis. Every
        /// pattern holds two parts, the second turned 180° from the first and clear of it by
        /// <paramref name="partSpacing"/>.
        /// </summary>
        public static List<List<(Pattern Pattern, NestDirection Dir)>> Build(
            Drawing drawing,
            double partSpacing,
            CancellationToken token
        )
        {
            // A drawing in its canonical frame shares the search with every fill of its
            // geometry at this spacing; the parts are always made for the drawing given.
            var recipe = Tolerance.IsEqualTo(drawing.Source?.Angle ?? 0.0, 0)
                ? BestFitCache.GetOrAddDerived(drawing, partSpacing, () => Search(drawing, partSpacing, token))
                : Search(drawing, partSpacing, token);

            var groups = new List<List<(Pattern, NestDirection)>>();
            foreach (var rows in recipe.Groups)
            {
                token.ThrowIfCancellationRequested();
                var group = new List<(Pattern, NestDirection)>();
                foreach (var row in rows)
                {
                    foreach (var pattern in Patterns(drawing, row))
                        group.Add((pattern, row.Axis));
                }

                groups.Add(group);
            }

            return groups;
        }

        /// <summary>Searches every rotation and row axis for its pairs; a rotation without a
        /// pair is left out.</summary>
        private static Recipe Search(Drawing drawing, double partSpacing, CancellationToken token)
        {
            var groups = new List<List<Row>>();
            foreach (var rotation in Rotations(drawing))
            {
                token.ThrowIfCancellationRequested();
                var group = new List<Row>();
                foreach (var axis in new[] { NestDirection.Horizontal, NestDirection.Vertical })
                {
                    var shifts = FindShifts(drawing, rotation, axis, partSpacing);
                    if (shifts.Length > 0)
                        group.Add(new Row(rotation, axis, shifts));
                }

                if (group.Count > 0)
                    groups.Add(group);
            }

            return new Recipe(groups);
        }

        /// <summary>The row's pairs as patterns of parts of <paramref name="drawing"/>, built
        /// by the steps the search placed them with.</summary>
        private static List<Pattern> Patterns(Drawing drawing, Row row)
        {
            var first = Part.CreateAtOrigin(drawing, row.Rotation);
            var reversed = Part.CreateAtOrigin(drawing, Angle.NormalizeRad(row.Rotation + System.Math.PI));
            var result = new List<Pattern>(row.Shifts.Length);
            foreach (var shift in row.Shifts)
                result.Add(Pair(first, reversed, shift));
            return result;
        }

        /// <summary>The first part, and the reversed part moved beside the row and slid onto it.</summary>
        private static Pattern Pair(Part first, Part reversed, Shift shift)
        {
            var moved = reversed.CloneAtOffset(shift.Offset).CloneAtOffset(shift.Slide);
            var pattern = new Pattern();
            pattern.Parts.Add((Part)first.Clone());
            pattern.Parts.Add(moved);
            foreach (var part in pattern.Parts)
                part.UpdateBounds();
            pattern.UpdateBounds();
            return pattern;
        }

        /// <summary>
        /// Rotations that lay one of the part's longest hull edges along a sheet axis, in each of
        /// the four quarter turns.
        /// </summary>
        internal static List<double> Rotations(Drawing drawing)
        {
            var hullAngles = RotationAnalysis.FindHullEdgeAngles(
                new List<Part> { Part.CreateAtOrigin(drawing) }
            );

            var bases = new List<double>();
            foreach (var angle in hullAngles)
            {
                var quarter = Angle.NormalizeRad(angle) % Angle.HalfPI;
                if (bases.Any(b => SameQuarterAngle(b, quarter)))
                    continue;
                bases.Add(quarter);
                if (bases.Count == MaxHullAngles)
                    break;
            }

            var rotations = new List<double>();
            foreach (var quarter in bases)
            {
                for (var k = 0; k < 4; k++)
                    rotations.Add(Angle.NormalizeRad(quarter + k * Angle.HalfPI));
            }

            return rotations;
        }

        private static bool SameQuarterAngle(double a, double b)
        {
            var difference = System.Math.Abs(a - b);
            return difference < AngleTolerance || Angle.HalfPI - difference < AngleTolerance;
        }

        /// <summary>The pairs making the shallowest double rows at one rotation and row axis.</summary>
        private static Shift[] FindShifts(
            Drawing drawing,
            double rotation,
            NestDirection axis,
            double partSpacing
        )
        {
            var result = new List<Shift>();
            var first = Part.CreateAtOrigin(drawing, rotation);
            var reversed = Part.CreateAtOrigin(drawing, Angle.NormalizeRad(rotation + System.Math.PI));
            if (first.BoundingBox.Area() <= 0)
                return result.ToArray();

            var single = new Pattern();
            single.Parts.Add(first);
            single.UpdateBounds();

            var huge = new Box(0, 0, 1e6, 1e6);
            var pitch = new FillLinear(huge, partSpacing).CopyDistance(single, axis);
            if (!(pitch > Tolerance.Epsilon) || double.IsInfinity(pitch))
                return result.ToArray();

            var horizontal = axis == NestDirection.Horizontal;
            var halfSpacing = partSpacing / 2;
            var firstBox = first.BoundingBox;
            var reversedBox = reversed.BoundingBox;

            // Enough copies of the first part to meet the reversed one anywhere along a pitch.
            var reach = (horizontal ? reversedBox.Width : reversedBox.Length)
                / pitch;
            var copies = (int)System.Math.Ceiling(reach) + 1;
            var row = new List<Entity>();
            for (var k = -copies; k <= copies; k++)
            {
                var along = k * pitch;
                var copy = first.CloneAtOffset(horizontal ? new Vector(along, 0) : new Vector(0, along));
                row.AddRange(PartGeometry.GetOffsetPerimeterEntities(copy, halfSpacing));
            }

            // The reversed part starts clear of the row on its far side and slides back onto it.
            var push = horizontal ? PushDirection.Down : PushDirection.Left;
            var across = horizontal
                ? firstBox.Top - reversedBox.Bottom + partSpacing + 1
                : firstBox.Right - reversedBox.Left + partSpacing + 1;

            (Pattern Pattern, Shift Shift)? Place(double shift)
            {
                var offset = horizontal
                    ? new Vector(firstBox.Left - reversedBox.Left + shift, across)
                    : new Vector(across, firstBox.Bottom - reversedBox.Bottom + shift);
                var moved = reversed.CloneAtOffset(offset);
                var distance = SpatialQuery.DirectionalDistance(
                    PartGeometry.GetOffsetPerimeterEntities(moved, halfSpacing),
                    row,
                    push
                );
                if (distance >= double.MaxValue || distance < 0)
                    return null;

                var placed = new Shift(offset, horizontal ? new Vector(0, -distance) : new Vector(-distance, 0));
                return (Pair(first, reversed, placed), placed);
            }

            double Depth(Pattern pattern) =>
                horizontal ? pattern.BoundingBox.Width : pattern.BoundingBox.Length;

            var step = pitch / ShiftSamples;
            var sampled = new List<(double Shift, (Pattern Pattern, Shift Shift) Placed)>();
            for (var i = 0; i < ShiftSamples; i++)
            {
                if (Place(i * step) is { } placed)
                    sampled.Add((i * step, placed));
            }

            foreach (var (shift, coarse) in sampled.OrderBy(s => Depth(s.Placed.Pattern)).Take(KeptShifts))
            {
                var best = coarse;
                for (var j = -RefineSamples; j <= RefineSamples; j++)
                {
                    if (j == 0)
                        continue;
                    if (Place(shift + j * step / RefineSamples) is { } placed
                        && Depth(placed.Pattern) < Depth(best.Pattern) - Tolerance.Epsilon)
                        best = placed;
                }

                result.Add(best.Shift);
            }

            return result.ToArray();
        }
    }
}
