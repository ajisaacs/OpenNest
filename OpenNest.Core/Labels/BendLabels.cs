using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using OpenNest.Bending;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Labels
{
    /// <summary>
    /// Where one bend's group letter is etched: the circle's center and diameter, and the index
    /// of the bend in the list the spots were placed for.
    /// </summary>
    public readonly record struct BendLabelSpot(int Bend, Vector Center, double Diameter);

    /// <summary>
    /// The bend group letter etched beside each bend line, so a part whose bends fall in several
    /// groups shows which bend is which (the label box only lists the groups).
    /// </summary>
    /// <remarks>
    /// Each letter sits in a circle on flat material just past the bend's radius band, never on
    /// the line, where forming would deform it, and reads along the line. Placement depends only
    /// on the part and its bends (a label box is not needed, only kept clear of), so it is the
    /// same however the part is turned; only the letter depends on the part's rotation against
    /// the grain.
    /// </remarks>
    public static class BendLabels
    {
        /// <summary>
        /// A spot for each bend's letter that has room for one, in the frame of
        /// <paramref name="entities"/>, <paramref name="bends"/> and <paramref name="box"/> (the
        /// part's label box, kept clear of; null when it has none).
        /// </summary>
        /// <remarks>
        /// The circle is as big as the label text sizes (<see cref="LabelBoxOptions.Sizes"/>):
        /// half an inch across is tried first, then three eighths, then a quarter (12, 9 and
        /// 6 mm), each an eighth inch (3 mm) clear. For each size the stations along the bend are tried in order (beside each etch dash of an up
        /// bend long enough to have two, then the middle and the quarter points), on the left of
        /// the line and then the right; the first that fits is kept. A spot fits on material a
        /// clearance inside every edge, clear of every bend's band, of scribe marks and break
        /// cuts, of the label box and of the letters placed before it. A bend with no such spot
        /// gets none.
        /// </remarks>
        public static List<BendLabelSpot> Place(IEnumerable<Entity> entities, IReadOnlyList<Bend> bends, LabelBox box,
            Units units = Units.Inches)
        {
            var spots = new List<BendLabelSpot>();
            if (entities == null || bends == null || bends.Count == 0)
                return spots;

            var all = entities.ToList();
            var sizes = LabelBoxOptions.Sizes(units);
            var clearance = sizes[0].Clearance;
            var tolerance = System.Math.Max(clearance / 20, System.Math.Pow(10, -ClipperBridge.Precision));
            var free = LabelBoxFinder.Material(all, clearance, tolerance, out _);
            if (free == null || free.Count == 0)
                return spots;

            var blocked = new PathsD();
            foreach (var bend in bends)
            {
                if (IsBend(bend))
                    blocked.AddRange(LabelBoxFinder.Strip(bend.StartPoint, bend.EndPoint, Band(bend, clearance), tolerance));
            }

            foreach (var mark in all.Where(e => ScribeLayerRepair.IsMarkLayer(e.Layer) || e.Layer == SpecialLayers.Break))
                blocked.AddRange(Grown(mark, clearance, tolerance));

            if (box is { IsValid: true })
            {
                var corners = new PathD(box.Corners().Select(corner => new PointD(corner.X, corner.Y)));
                blocked.AddRange(Clipper.InflatePaths(new PathsD { corners }, clearance, JoinType.Miter, EndType.Polygon, 2.0,
                    ClipperBridge.Precision, tolerance));
            }

            free = Clipper.Difference(free, blocked, FillRule.NonZero, ClipperBridge.Precision);
            if (free.Count == 0)
                return spots;

            // Where a circle's center may be, per size: the free region eroded by its radius.
            var centers = new PathsD[sizes.Count];
            for (var index = 0; index < bends.Count; index++)
            {
                var bend = bends[index];
                if (!IsBend(bend))
                    continue;

                var length = bend.Length;
                var along = new Vector((bend.EndPoint.X - bend.StartPoint.X) / length, (bend.EndPoint.Y - bend.StartPoint.Y) / length);
                var left = new Vector(-along.Y, along.X);
                var stations = Stations(bend);
                var placed = false;
                for (var size = 0; size < sizes.Count && !placed; size++)
                {
                    var diameter = sizes[size].Height;
                    var radius = diameter / 2;
                    centers[size] ??= Clipper.InflatePaths(free, -radius, JoinType.Round, EndType.Polygon, 2.0,
                        ClipperBridge.Precision, tolerance);

                    // Just past the band, so the erosion's rounding never rejects the spot.
                    var away = Band(bend, clearance) + radius + 2 * tolerance;
                    foreach (var station in stations)
                    {
                        foreach (var side in new[] { 1, -1 })
                        {
                            var center = new Vector(
                                bend.StartPoint.X + along.X * station + left.X * side * away,
                                bend.StartPoint.Y + along.Y * station + left.Y * side * away);
                            if (!Inside(centers[size], center)
                                || spots.Any(s => s.Center.DistanceTo(center) < s.Diameter / 2 + radius + clearance))
                                continue;

                            spots.Add(new BendLabelSpot(index, center, diameter));
                            placed = true;
                            break;
                        }
                        if (placed)
                            break;
                    }
                }
            }
            return spots;
        }

        /// <summary>
        /// The strokes of each spot's circled letter, in the spots' frame, reading along its bend
        /// line. The letter is the bend's group for a part placed at
        /// <paramref name="partRotation"/> on a sheet whose grain runs at <paramref name="grainAngle"/>;
        /// <paramref name="bends"/> are the bends the spots were placed for, unrotated.
        /// </summary>
        public static List<Stroke> Layout(IEnumerable<BendLabelSpot> spots, IReadOnlyList<Bend> bends, double partRotation,
            double grainAngle)
        {
            var strokes = new List<Stroke>();
            if (spots == null || bends == null)
                return strokes;

            foreach (var spot in spots)
            {
                if (spot.Bend < 0 || spot.Bend >= bends.Count || !IsBend(bends[spot.Bend]))
                    continue;
                var line = bends[spot.Bend].LineAngle;
                var letter = PartLabels.GroupLetter(line + partRotation, grainAngle);
                // Along the line, turned at most a quarter so it never reads upside down.
                strokes.AddRange(PartLabels.CircledLetter(letter, spot.Center, spot.Diameter,
                    LabelBox.ReadableRotation(line)));
            }
            return strokes;
        }

        /// <summary>
        /// The spots for <paramref name="drawing"/>'s bends, in its program's frame before any
        /// rotation (the source frame less the program's offset). Empty without bends or a
        /// program; a label box is only kept clear of. The same for every part of the drawing, so callers may keep it.
        /// </summary>
        public static List<BendLabelSpot> Spots(Drawing drawing, Units units = Units.Inches)
        {
            if (drawing?.Program == null || drawing.Bends == null || drawing.Bends.Count == 0)
                return new List<BendLabelSpot>();
            var box = drawing.LabelBox is { IsValid: true } valid ? valid : null;

            // The program's geometry is in its own frame: the source frame less the offset, turned
            // by the program's rotation. The bends and the box go there, and the spots come back.
            var offset = drawing.Source?.Offset ?? Vector.Zero;
            var turn = drawing.Program.Rotation;
            Vector ToProgram(Vector point)
            {
                var moved = point - offset;
                return turn == 0 ? moved : moved.Rotate(turn);
            }

            var bends = drawing.Bends.Select(bend => bend == null ? null : new Bend
            {
                StartPoint = ToProgram(bend.StartPoint),
                EndPoint = ToProgram(bend.EndPoint),
                Direction = bend.Direction,
                Angle = bend.Angle,
                Radius = bend.Radius,
            }).ToList();
            var spots = Place(ConvertProgram.ToGeometry(drawing.Program), bends,
                box == null ? null : new LabelBox(ToProgram(box.Center), box.Width, box.Height, box.Rotation + turn),
                units);
            return turn == 0 ? spots : spots.ConvertAll(spot => spot with { Center = spot.Center.Rotate(-turn) });
        }

        /// <summary>
        /// The etch strokes of the bend letters at <paramref name="spots"/> (from <see cref="Spots"/>)
        /// for a part placed at <paramref name="partRotation"/>, in the drawing's program frame
        /// turned with the part.
        /// </summary>
        public static List<Stroke> Strokes(Drawing drawing, IEnumerable<BendLabelSpot> spots, double partRotation,
            double grainAngle)
        {
            if (drawing == null || !double.IsFinite(partRotation) || !double.IsFinite(grainAngle))
                return new List<Stroke>();

            var strokes = Layout(spots, drawing.Bends, partRotation, grainAngle);
            return partRotation == 0
                ? strokes
                : strokes.ConvertAll(stroke => stroke.Map(point => point.Rotate(partRotation)));
        }

        /// <summary>
        /// Appends the bend letters to <paramref name="program"/> as scribe (etch) moves, as
        /// <see cref="PartLabels.AddTo"/> does the label. Returns false and leaves the program
        /// unchanged when there is nothing to etch.
        /// </summary>
        public static bool AddTo(Program program, Drawing drawing, IEnumerable<BendLabelSpot> spots, double partRotation,
            double grainAngle) =>
            PartLabels.Append(program, Strokes(drawing, spots, partRotation, grainAngle));

        private static bool IsBend(Bend bend) => bend != null && bend.Length > Tolerance.Epsilon;

        // Half the width kept clear along a bend line: the clearance, or half the arc the bend
        // rolls the material through when that is wider, so the letter stays on flat material.
        private static double Band(Bend bend, double clearance) =>
            bend.Radius is > 0 and var radius && bend.AngleRadians > 0
                ? System.Math.Max(clearance, radius * bend.AngleRadians / 2)
                : clearance;

        // Distances from the bend's start to try, in order.
        private static List<double> Stations(Bend bend)
        {
            var length = bend.Length;
            var stations = new List<double>();
            var dashes = bend.GetEtchEntities();
            if (dashes.Count == 2)
            {
                foreach (var dash in dashes)
                {
                    var middle = new Vector((dash.StartPoint.X + dash.EndPoint.X) / 2, (dash.StartPoint.Y + dash.EndPoint.Y) / 2);
                    stations.Add(bend.StartPoint.DistanceTo(middle));
                }
            }
            stations.Add(length / 2);
            stations.Add(length / 4);
            stations.Add(length * 3 / 4);
            return stations;
        }

        // A mark or break cut grown by the clearance. A curve is flattened outside itself and
        // closed by its chord, so it never under-covers.
        private static PathsD Grown(Entity entity, double clearance, double tolerance)
        {
            if (entity is Line line)
                return LabelBoxFinder.Strip(line.StartPoint, line.EndPoint, clearance, tolerance);

            var shape = new Shape();
            shape.Entities.Add(entity);
            var path = ClipperBridge.ToPath(ClipperBridge.Flatten(shape, tolerance, circumscribe: true), Vector.Zero);
            return path.Count < 2
                ? new PathsD()
                : Clipper.InflatePaths(new PathsD { path }, clearance, JoinType.Round, EndType.Joined, 2.0,
                    ClipperBridge.Precision, tolerance);
        }

        // Even-odd over the region's paths: inside an outline and not inside a hole in it.
        private static bool Inside(PathsD region, Vector point)
        {
            var at = new PointD(point.X, point.Y);
            var count = 0;
            foreach (var path in region)
            {
                if (Clipper.PointInPolygon(at, path) != PointInPolygonResult.IsOutside)
                    count++;
            }
            return count % 2 == 1;
        }
    }
}
