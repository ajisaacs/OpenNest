using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using OpenNest.Bending;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Labels
{
    /// <summary>What <see cref="LabelBoxFinder"/> must fit, in the drawing's units.</summary>
    public sealed class LabelBoxOptions
    {
        /// <summary>Characters in the longest label: a part number and a few bend group letters ("1234-AB").</summary>
        public const int DefaultCharacters = 7;

        /// <summary>Etch stroke advance as a share of the character height.</summary>
        public const double CharacterAdvance = 0.75;

        /// <summary>The label's length along the text.</summary>
        public double Width { get; set; }

        /// <summary>The label's height across the text.</summary>
        public double Height { get; set; }

        /// <summary>The clearance kept from the part's edges, holes and cutouts.</summary>
        public double Clearance { get; set; }

        /// <summary>The clearance kept from each side of a bend line; <see cref="Clearance"/> when null.</summary>
        public double? BendClearance { get; set; }

        /// <summary>A label of <paramref name="characters"/> characters in a stroke font of height <paramref name="characterHeight"/>.</summary>
        public static LabelBoxOptions ForText(int characters, double characterHeight, double clearance) =>
            new()
            {
                Width = characters * characterHeight * CharacterAdvance,
                Height = characterHeight,
                Clearance = clearance,
            };

        /// <summary>A quarter inch (6 mm) label of <see cref="DefaultCharacters"/> characters, an eighth inch (3 mm) from anything else.</summary>
        public static LabelBoxOptions Default(Units units) => units == Units.Millimeters
            ? ForText(DefaultCharacters, 6, 3)
            : ForText(DefaultCharacters, 0.25, 0.125);
    }

    /// <summary>
    /// Finds where a part number label can be etched: a rectangle of the label's size on the part's
    /// flat material, clear of its edges, holes, cutouts and bend lines.
    /// </summary>
    /// <remarks>
    /// The material is eroded by the etch clearance (the same Clipper inward offset the pierce
    /// search uses on a cutout), and a band either side of each bend line is cut out of what is
    /// left. For each orientation tried, the largest empty rectangles of the rotated region are
    /// searched (<see cref="MaximalRectangles"/>) and the one with the most room around the label
    /// wins; the label-sized box is centered in it. Orientations are the axes and the direction of
    /// each bend and of the longest straight edge, plus the quarter turn of each, with text turned
    /// at most a quarter so it never reads upside down. Earlier orientations win unless a later
    /// one has a clearly roomier rectangle, so the result is deterministic and favors the
    /// horizontal. Rectangles are staircased against slanted and curved edges, so they never cross
    /// the clearance.
    /// </remarks>
    public static class LabelBoxFinder
    {
        // A later orientation must have this much more room than the best so far to replace it.
        private const double ImprovementFactor = 1.05;

        private const int Divisions = 128;
        private const int RectanglesPerOrientation = 8;
        private static readonly double AngleMerge = Angle.ToRadians(0.5);

        /// <summary>
        /// A box for the label, or null when nothing fits. <paramref name="entities"/> are the part as
        /// it will be cut; marks and rapids are not material, and a tabbed cutout's slug counts as
        /// part (<see cref="NestingMaterial.ForNesting"/>).
        /// </summary>
        /// <param name="entities">The part as it will be cut.</param>
        /// <param name="bends">Bend lines to keep clear of.</param>
        /// <param name="options">The label size and clearances.</param>
        /// <param name="keepOut">Closed outlines inside the part's material that must stay clear, each
        /// grown by the clearance: a breakoff's scrap, which is snapped off after forming.</param>
        public static LabelBox Find(IEnumerable<Entity> entities, IEnumerable<Bend> bends, LabelBoxOptions options,
            IEnumerable<IEnumerable<Entity>> keepOut = null)
        {
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(options);
            if (!(options.Width > 0) || !(options.Height > 0) || !double.IsFinite(options.Width)
                || !double.IsFinite(options.Height) || !(options.Clearance >= 0) || !double.IsFinite(options.Clearance))
                throw new ArgumentException("The label size and clearance must be finite and positive.", nameof(options));

            var material = entities.Where(e => !ScribeLayerRepair.IsMarkLayer(e.Layer)).ForNesting();
            var shapes = ShapeBuilder.GetShapes(material).Where(s => s.IsClosed()).ToList();
            if (shapes.Count == 0)
                return null;

            var bendList = (bends ?? Enumerable.Empty<Bend>())
                .Where(b => b.Length > Tolerance.Epsilon)
                .ToList();

            var clearance = options.Clearance;
            var bendClearance = options.BendClearance ?? clearance;
            var tolerance = System.Math.Max(System.Math.Min(clearance, bendClearance) / 20,
                System.Math.Pow(10, -ClipperBridge.Precision));

            // The perimeter is flattened inside the true curve and cutouts outside it, so the
            // region never reaches past the real material.
            var region = ClipperBridge.ToRegion(ShapeProfile.FromShapes(shapes), tolerance, circumscribe: false);
            var free = clearance > 0
                ? Clipper.InflatePaths(region, -(clearance + tolerance), JoinType.Round, EndType.Polygon, 2.0,
                    ClipperBridge.Precision, tolerance)
                : region;

            var bands = new PathsD();
            foreach (var bend in bendList)
            {
                bands.AddRange(Clipper.InflatePaths(
                    new PathsD { new PathD { new PointD(bend.StartPoint.X, bend.StartPoint.Y), new PointD(bend.EndPoint.X, bend.EndPoint.Y) } },
                    bendClearance + tolerance, JoinType.Round, EndType.Round, 2.0, ClipperBridge.Precision, tolerance));
            }

            // A kept-out outline is flattened outside its curve so it never under-covers.
            foreach (var outline in keepOut ?? Enumerable.Empty<IEnumerable<Entity>>())
            {
                foreach (var shape in ShapeBuilder.GetShapes(outline.ToList()).Where(s => s.IsClosed()))
                {
                    var path = new PathsD { ClipperBridge.ToPath(ClipperBridge.Flatten(shape, tolerance, circumscribe: true), positive: true) };
                    bands.AddRange(Clipper.InflatePaths(path, clearance + tolerance, JoinType.Round, EndType.Polygon, 2.0,
                        ClipperBridge.Precision, tolerance));
                }
            }

            if (bands.Count > 0)
                free = Clipper.Difference(free, bands, FillRule.NonZero, ClipperBridge.Precision);

            if (free.Count == 0)
                return null;

            LabelBox best = null;
            var bestRoom = 0.0;
            foreach (var angle in Orientations(bendList, shapes))
            {
                var room = Best(Rotate(free, -angle), options, out var center);
                if (room < 1 - Tolerance.Epsilon || !(room > bestRoom * ImprovementFactor))
                    continue;

                bestRoom = room;
                best = new LabelBox(Turn(center, angle), options.Width, options.Height, angle);
            }
            return best;
        }

        // The most room any large rectangle has around the label: how many times the label fits in
        // its tighter side. Under 1 means the label does not fit.
        private static double Best(PathsD region, LabelBoxOptions options, out Vector center)
        {
            center = default;
            var best = 0.0;
            foreach (var rect in MaximalRectangles.InRegion(region, 0, Divisions, RectanglesPerOrientation))
            {
                // A Box's Length runs along X and its Width along Y.
                var room = System.Math.Min(rect.Length / options.Width, rect.Width / options.Height);
                if (room > best)
                {
                    best = room;
                    center = rect.Center;
                }
            }
            return best;
        }

        // Axes first (text horizontal reads best on the sheet), then the longest bend, the other
        // bends, and the longest straight edge, each with its quarter turn. Text is never turned
        // past a quarter: angles are taken in (-π/2, π/2].
        private static List<double> Orientations(List<Bend> bends, List<Shape> shapes)
        {
            var raw = new List<double> { 0, System.Math.PI / 2 };
            foreach (var bend in bends.OrderByDescending(b => b.Length))
                raw.Add(bend.LineAngle);

            var edge = shapes.SelectMany(s => s.Entities).OfType<Line>()
                .OrderByDescending(l => l.Length)
                .FirstOrDefault();
            if (edge != null)
                raw.Add(edge.StartPoint.AngleTo(edge.EndPoint));

            var angles = new List<double>();
            foreach (var value in raw)
            {
                foreach (var turn in new[] { 0.0, System.Math.PI / 2 })
                {
                    var angle = Quarter(value + turn);
                    if (!angles.Any(a => System.Math.Abs(a - angle) < AngleMerge))
                        angles.Add(angle);
                }
            }
            return angles;
        }

        // The equivalent angle in (-π/2, π/2]: a half turn only flips the text.
        private static double Quarter(double angle)
        {
            var half = System.Math.PI;
            angle -= System.Math.Floor(angle / half) * half; // [0, π)
            return angle > half / 2 ? angle - half : angle;
        }

        private static PathsD Rotate(PathsD paths, double angle)
        {
            var cos = PortableMath.Cos(angle);
            var sin = PortableMath.Sin(angle);
            var result = new PathsD(paths.Count);
            foreach (var path in paths)
            {
                var rotated = new PathD(path.Count);
                foreach (var point in path)
                    rotated.Add(new PointD(point.x * cos - point.y * sin, point.x * sin + point.y * cos));
                result.Add(rotated);
            }
            return result;
        }

        private static Vector Turn(Vector point, double angle)
        {
            var cos = PortableMath.Cos(angle);
            var sin = PortableMath.Sin(angle);
            return new Vector(point.X * cos - point.Y * sin, point.X * sin + point.Y * cos);
        }
    }
}
