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
    /// left. For each orientation tried, the label gets the widest margin it can have on every side
    /// beyond that clearance: the largest m for which some empty rectangle of the rotated region
    /// (<see cref="MaximalRectangles"/>) holds the label grown by m all round, found by bisection.
    /// The box is centered in that rectangle, so it sits where it has the most room, not halfway
    /// along a long, narrow strip. Orientations are the axes and the direction of each bend and of
    /// the longest straight edge, plus the quarter turn of each, with text turned at most a
    /// quarter so it never reads upside down. Horizontal is tried first; a later orientation wins
    /// only with a clearly wider margin, and a turn off the axes only with half again the margin
    /// plus half the label height, so the result is deterministic and favors level text.
    /// Rectangles are staircased against slanted and curved edges, so they never cross the
    /// clearance.
    /// </remarks>
    public static class LabelBoxFinder
    {
        // A later orientation must give this much more margin than the best so far to replace it.
        private const double ImprovementFactor = 1.05;

        // An off-axis orientation (a bend's or an edge's direction) replaces horizontal or
        // vertical text only with this much more margin, plus this share of the label height.
        private const double OffAxisFactor = 1.5;
        private const double OffAxisSlack = 0.5;

        private const int Divisions = 128;
        private const int RectanglesPerStep = 16;
        private const int MaxBisections = 24;
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
            var bestMargin = 0.0;
            var bestOnAxis = false;
            var precision = System.Math.Max(tolerance, options.Height / 100);
            foreach (var (angle, onAxis) in Orientations(bendList, shapes))
            {
                var margin = Roomiest(Rotate(free, -angle), options, precision, out var center);
                if (margin < 0)
                    continue;

                if (best != null)
                {
                    // Text on a turn the part has no reason for (the axes) reads best; an
                    // off-axis turn must give clearly more room, not just a little.
                    var required = bestOnAxis && !onAxis
                        ? bestMargin * OffAxisFactor + options.Height * OffAxisSlack
                        : bestMargin * ImprovementFactor;
                    if (!(margin > required + Tolerance.Epsilon))
                        continue;
                }

                bestMargin = margin;
                bestOnAxis = onAxis;
                best = new LabelBox(Turn(center, angle), options.Width, options.Height, angle);
            }
            return best;
        }

        // The widest margin the label can have on every side, beyond the clearance already taken
        // out of the region, with the label upright in the region's frame; negative when it does
        // not fit at all. The label sits in the middle of the rectangle that gives it that margin.
        // A box of the label grown by m on each side fits for every m up to the answer, so the
        // largest is found by bisection; each step asks only for rectangles at least that thick,
        // so long thin strips are never what holds the answer back.
        private static double Roomiest(PathsD region, LabelBoxOptions options, double precision, out Vector center)
        {
            center = default;
            if (!Fits(region, options, 0, out var rect))
                return -1;

            var lo = Margin(rect, options);
            center = rect.Center;

            var bounds = Clipper.GetBounds(region);
            var hi = System.Math.Min(bounds.Width - options.Width, bounds.Height - options.Height) / 2;
            for (var i = 0; i < MaxBisections && hi - lo > precision; i++)
            {
                var mid = (lo + hi) / 2;
                if (Fits(region, options, mid, out rect))
                {
                    lo = System.Math.Max(mid, Margin(rect, options));
                    center = rect.Center;
                }
                else
                {
                    hi = mid;
                }
            }
            return lo;
        }

        // The largest-margin rectangle that holds the label grown by margin on each side.
        private static bool Fits(PathsD region, LabelBoxOptions options, double margin, out Box best)
        {
            best = null;
            var bestMargin = double.NegativeInfinity;
            var thickness = options.Height + 2 * margin;
            foreach (var rect in MaximalRectangles.InRegion(region, thickness, Divisions, RectanglesPerStep))
            {
                // A Box's Length runs along X and its Width along Y.
                if (rect.Length < options.Width + 2 * margin || rect.Width < thickness)
                    continue;
                var room = Margin(rect, options);
                if (room > bestMargin)
                {
                    bestMargin = room;
                    best = rect;
                }
            }
            return best != null;
        }

        private static double Margin(Box rect, LabelBoxOptions options) =>
            System.Math.Min(rect.Length - options.Width, rect.Width - options.Height) / 2;

        // Axes first (text horizontal reads best on the sheet), then the longest bend, the other
        // bends, and the longest straight edge, each with its quarter turn. Text is never turned
        // past a quarter: angles are taken in (-π/2, π/2].
        private static List<(double Angle, bool OnAxis)> Orientations(List<Bend> bends, List<Shape> shapes)
        {
            var raw = new List<double> { 0, System.Math.PI / 2 };
            foreach (var bend in bends.OrderByDescending(b => b.Length))
                raw.Add(bend.LineAngle);

            var edge = shapes.SelectMany(s => s.Entities).OfType<Line>()
                .OrderByDescending(l => l.Length)
                .FirstOrDefault();
            if (edge != null)
                raw.Add(edge.StartPoint.AngleTo(edge.EndPoint));

            var angles = new List<(double Angle, bool OnAxis)>();
            foreach (var value in raw)
            {
                foreach (var turn in new[] { 0.0, System.Math.PI / 2 })
                {
                    var angle = Quarter(value + turn);
                    if (!angles.Any(a => System.Math.Abs(a.Angle - angle) < AngleMerge))
                        angles.Add((angle, angles.Count < 2));
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
