using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Geometry;

namespace OpenNest.Engine.Fill
{
    public static partial class Expander
    {
        /// <summary>
        /// The lattice a selection repeats on. Parts of one drawing at one
        /// rotation form a motif group; every group sits on the same two
        /// translations <see cref="A"/> and <see cref="B"/>, so a fill of
        /// interlocked pairs is one pattern with a two-part motif.
        /// </summary>
        internal sealed class RepeatingPattern
        {
            public Vector A;

            /// <summary>Second translation; <see cref="Vector.Zero"/> for a single row
            /// or for <see cref="Rows"/>.</summary>
            public Vector B;

            /// <summary>
            /// True for rows that share the pitch <see cref="A"/> but stack at
            /// their own spacing, such as staggered rows topped by straight ones.
            /// Each part is its own lattice point.
            /// </summary>
            public bool Rows;

            /// <summary>
            /// Per selected part, the lattice point of its motif copy. Parts of
            /// one copy share it, so stretching the lattice keeps every motif rigid.
            /// </summary>
            public Vector[] LatticePoints;

            // Lattice coordinates may miss a whole number by this fraction of a
            // pitch, which absorbs the four-decimal rounding of saved positions.
            private const double CoordinateTolerance = 0.02;

            // Rotations closer than this are the same motif orientation.
            private const double RotationTolerance = 1e-6;

            /// <summary>
            /// The pattern the parts repeat on, or null when they do not form one.
            /// A pattern must over-determine its lattice: a row needs three parts
            /// of one motif group and a grid four, since any two or three points
            /// trivially fit some lattice. Every group needs at least two parts.
            /// A single motif group that fits no one lattice may still form
            /// <see cref="Rows"/>.
            /// </summary>
            public static RepeatingPattern Detect(IReadOnlyList<Part> parts)
            {
                if (parts == null || parts.Count < 3 || parts.Any(p => p.BaseDrawing == null || p.BaseDrawing.IsCutOff))
                    return null;

                var centers = parts.Select(p => p.BoundingBox.Center).ToArray();
                var groups = Group(parts);
                if (groups.Any(g => g.Count < 2))
                    return null;

                return DetectLattice(groups, centers)
                    ?? DetectRows(parts, groups, centers, horizontal: true)
                    ?? DetectRows(parts, groups, centers, horizontal: false);
            }

            private static RepeatingPattern DetectLattice(List<List<int>> groups, Vector[] centers)
            {
                if (!TryBasis(groups, centers, out var a, out var b))
                    return null;

                var rank = b.X == 0 && b.Y == 0 ? 1 : 2;
                if (groups.Max(g => g.Count) < rank + 2)
                    return null;

                // Each group's copies pair with the first group's copies through
                // its part nearest that group's origin: the reduced motif offset,
                // so an interlocked partner moves with its own copy.
                var origin = centers[groups[0][0]];
                var points = new Vector[centers.Length];
                foreach (var group in groups)
                {
                    var groupOrigin = centers[group.OrderBy(i => centers[i].DistanceTo(origin)).First()];
                    foreach (var index in group)
                    {
                        if (!TryCoordinates(centers[index] - groupOrigin, a, b, out var u, out var v))
                            return null;
                        points[index] = origin + a * u + b * v;
                    }
                }

                return new RepeatingPattern { A = a, B = b, LatticePoints = points };
            }

            /// <summary>
            /// Rows of one motif along X (or Y): at least two rows, each of at
            /// least two parts and one of at least three, whose steps along the
            /// row are all whole multiples of one shared pitch. Rows may stack at
            /// any spacing and offset, so stretching moves each row rigidly.
            /// </summary>
            private static RepeatingPattern DetectRows(
                IReadOnlyList<Part> parts, List<List<int>> groups, Vector[] centers, bool horizontal)
            {
                if (groups.Count != 1)
                    return null;
                double Along(Vector v) => horizontal ? v.X : v.Y;
                double Across(Vector v) => horizontal ? v.Y : v.X;

                // Parts of one row share their cross coordinate to within a
                // sliver of the part's own size.
                var box = parts[0].BoundingBox;
                var rowTolerance = CoordinateTolerance * System.Math.Min(box.Length, box.Width);
                var rows = new List<List<Vector>>();
                foreach (var center in centers.OrderBy(Across))
                {
                    if (rows.Count == 0 || Across(center) - Across(rows[^1][0]) > rowTolerance)
                        rows.Add(new List<Vector>());
                    rows[^1].Add(center);
                }
                if (rows.Count < 2 || rows.Any(r => r.Count < 2) || rows.Max(r => r.Count) < 3)
                    return null;

                var steps = rows
                    .SelectMany(r =>
                    {
                        var along = r.Select(Along).OrderBy(x => x).ToList();
                        return along.Skip(1).Select((x, i) => x - along[i]);
                    })
                    .ToList();
                var pitch = steps.Min();
                if (pitch <= rowTolerance)
                    return null;
                if (steps.Any(step => System.Math.Abs(step / pitch - System.Math.Round(step / pitch)) > CoordinateTolerance))
                    return null;

                return new RepeatingPattern
                {
                    A = horizontal ? new Vector(pitch, 0) : new Vector(0, pitch),
                    B = Vector.Zero,
                    Rows = true,
                    LatticePoints = centers.ToArray(),
                };
            }

            private static List<List<int>> Group(IReadOnlyList<Part> parts)
            {
                var groups = new List<List<int>>();
                for (var i = 0; i < parts.Count; i++)
                {
                    var group = groups.FirstOrDefault(g =>
                        ReferenceEquals(parts[g[0]].BaseDrawing, parts[i].BaseDrawing)
                        && SameRotation(parts[g[0]].Rotation, parts[i].Rotation));
                    if (group == null)
                        groups.Add(group = new List<int>());
                    group.Add(i);
                }
                return groups;
            }

            private static bool SameRotation(double a, double b)
            {
                var difference = OpenNest.Math.Angle.NormalizeRad(a - b);
                return difference < RotationTolerance || OpenNest.Math.Angle.TwoPI - difference < RotationTolerance;
            }

            /// <summary>
            /// The shortest translation between two parts of one group, then the
            /// shortest one not parallel to it. Coincident parts are no pattern.
            /// </summary>
            private static bool TryBasis(List<List<int>> groups, Vector[] centers, out Vector a, out Vector b)
            {
                a = Vector.Zero;
                b = Vector.Zero;
                var shortest = double.MaxValue;
                foreach (var d in Translations(groups, centers))
                {
                    var length = Length(d);
                    if (length < 1e-6)
                        return false;
                    if (length < shortest)
                    {
                        shortest = length;
                        a = d;
                    }
                }

                var second = double.MaxValue;
                foreach (var d in Translations(groups, centers))
                {
                    var length = Length(d);
                    if (System.Math.Abs(Cross(a, d)) <= 1e-3 * shortest * length)
                        continue;
                    if (length < second)
                    {
                        second = length;
                        b = d;
                    }
                }
                return true;
            }

            private static IEnumerable<Vector> Translations(List<List<int>> groups, Vector[] centers)
            {
                foreach (var group in groups)
                    for (var i = 0; i < group.Count; i++)
                        for (var j = i + 1; j < group.Count; j++)
                            yield return centers[group[j]] - centers[group[i]];
            }

            private static bool TryCoordinates(Vector d, Vector a, Vector b, out double u, out double v)
            {
                if (b.X == 0 && b.Y == 0)
                {
                    // A row: on the line through a, at a whole multiple of it.
                    var lengthA = Length(a);
                    var exact = (d.X * a.X + d.Y * a.Y) / (lengthA * lengthA);
                    u = System.Math.Round(exact);
                    v = 0;
                    var offRow = System.Math.Abs(Cross(a, d)) / lengthA;
                    return offRow <= CoordinateTolerance * lengthA
                        && System.Math.Abs(exact - u) <= CoordinateTolerance;
                }

                var det = Cross(a, b);
                var uExact = Cross(d, b) / det;
                var vExact = Cross(a, d) / det;
                u = System.Math.Round(uExact);
                v = System.Math.Round(vExact);
                return System.Math.Abs(uExact - u) <= CoordinateTolerance
                    && System.Math.Abs(vExact - v) <= CoordinateTolerance;
            }

            private static double Cross(Vector a, Vector b) => a.X * b.Y - a.Y * b.X;

            private static double Length(Vector v) => System.Math.Sqrt(v.X * v.X + v.Y * v.Y);
        }

        /// <summary>
        /// Spreads a selection that repeats on a lattice by stretching the
        /// lattice along X, then Y, as far as the work area and every clearance
        /// floor allow, so the pattern survives instead of being pushed apart
        /// pair by pair. Returns null when the selection is no pattern, overlaps
        /// or starts outside the work area; the pair search handles those.
        /// </summary>
        private static Result StretchPattern(
            SeparationContext context,
            List<Vector> entry,
            List<Part> selected,
            double sMin,
            Options opt,
            CancellationToken token
        )
        {
            if (sMin <= 0 || context.WorkAreaViolations(entry).Count > 0)
                return null;
            var pattern = RepeatingPattern.Detect(selected);
            if (pattern == null)
                return null;

            // The tightest selected pair sets the floor: no selected gap closes
            // below it, and gaps to other parts keep the usual floors at it.
            var floor = double.MaxValue;
            foreach (var pair in context.Pairs)
                if (context.IsMover(pair.IndexB))
                    floor = System.Math.Min(floor, pair.EntryFloor);
            if (!(floor > 0) || floor == double.MaxValue)
                return null;

            var stretch = new PatternStretch(context, entry, selected, pattern.LatticePoints, floor);
            if (!stretch.Spread(horizontal: true, opt.Tolerance, token)
                || !stretch.Spread(horizontal: false, opt.Tolerance, token))
                return new Result { Canceled = true };

            var positions = stretch.Positions();
            if (!context.MeetsEntryFloors(positions, entry, floor))
                throw new InvalidOperationException("Pattern stretch violates an entry clearance floor.");

            double measured;
            try
            {
                measured = MinimumPairClearance(context, positions, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return new Result { Canceled = true };
            }
            if (token.IsCancellationRequested)
                return new Result { Canceled = true };

            Apply(selected, entry, positions);
            return new Result
            {
                AchievedSpacing = System.Math.Max(0, measured),
                KeptPattern = true,
                PatternMoved = stretch.ScaleX > 1 || stretch.ScaleY > 1,
            };
        }

        /// <summary>
        /// Per-axis scale of a pattern's lattice points about their center, plus
        /// the shift that keeps the stretched selection inside the work area.
        /// A part moves by the displacement of its lattice point.
        /// </summary>
        private sealed class PatternStretch
        {
            private readonly SeparationContext context;
            private readonly List<Vector> entry;
            private readonly Box[] bounds;
            private readonly Vector[] lattice;
            private readonly Vector center;
            private readonly double floor;

            public double ScaleX = 1, ScaleY = 1;
            private double shiftX, shiftY;

            public PatternStretch(
                SeparationContext context, List<Vector> entry, List<Part> selected,
                Vector[] lattice, double floor)
            {
                this.context = context;
                this.entry = entry;
                this.lattice = lattice;
                this.floor = floor;
                bounds = selected.Select(p => p.BoundingBox).ToArray();
                center = new Vector(
                    (lattice.Min(p => p.X) + lattice.Max(p => p.X)) / 2,
                    (lattice.Min(p => p.Y) + lattice.Max(p => p.Y)) / 2);
            }

            public List<Vector> Positions() => Positions(ScaleX, shiftX, ScaleY, shiftY);

            private List<Vector> Positions(double scaleX, double offsetX, double scaleY, double offsetY)
            {
                var positions = new List<Vector>(entry);
                for (var i = 0; i < lattice.Length; i++)
                    positions[i] = new Vector(
                        entry[i].X + (scaleX - 1) * (lattice[i].X - center.X) + offsetX,
                        entry[i].Y + (scaleY - 1) * (lattice[i].Y - center.Y) + offsetY);
                return positions;
            }

            /// <summary>
            /// True when no selected part passes through a fixed part, cut-off or
            /// clamp on its straight way from the entry layout, so a pattern never
            /// spreads to the far side of material it started beside.
            /// </summary>
            private bool Unobstructed(List<Vector> positions)
            {
                if (!context.HasWalls)
                    return true;
                for (var i = 0; i < lattice.Length; i++)
                {
                    var move = positions[i] - entry[i];
                    var distance = System.Math.Sqrt(move.X * move.X + move.Y * move.Y);
                    if (distance <= 1e-9)
                        continue;
                    var room = context.ClipToMaterial(i, entry, move / distance, distance, wallsOnly: true);
                    if (room < distance - 1e-6)
                        return false;
                }
                return true;
            }

            private enum Placement { Center, Low, High }

            /// <summary>
            /// Finds the largest scale along one axis whose stretched selection
            /// fits the work area and keeps every floor, trying the selection
            /// centered, then held at its low and high edges. Returns false when
            /// canceled.
            /// </summary>
            public bool Spread(bool horizontal, double tolerance, CancellationToken token)
            {
                double Of(Vector v) => horizontal ? v.X : v.Y;
                double LowOf(Box box) => horizontal ? box.Left : box.Bottom;
                double HighOf(Box box) => horizontal ? box.Right : box.Top;

                var mid = Of(center);
                var reach = lattice.Max(Of) - lattice.Min(Of);
                if (reach <= tolerance)
                    return true; // one row or column along this axis

                var area = context.WorkArea;
                var areaLow = LowOf(area);
                var areaHigh = HighOf(area);
                var entryLow = bounds.Min(LowOf);
                var entryHigh = bounds.Max(HighOf);

                // Stretched extent with no shift; null when it is wider than the area.
                (double Low, double High)? Extent(double scale)
                {
                    var low = double.MaxValue;
                    var high = double.MinValue;
                    for (var i = 0; i < bounds.Length; i++)
                    {
                        var move = (scale - 1) * (Of(lattice[i]) - mid);
                        low = System.Math.Min(low, LowOf(bounds[i]) + move);
                        high = System.Math.Max(high, HighOf(bounds[i]) + move);
                    }
                    return high - low <= areaHigh - areaLow ? (low, high) : null;
                }

                double ShiftFor(double scale, Placement placement)
                {
                    var (low, high) = Extent(scale).Value;
                    var shift = placement switch
                    {
                        Placement.Low => entryLow - low,
                        Placement.High => entryHigh - high,
                        _ => (entryLow + entryHigh - low - high) / 2,
                    };
                    return System.Math.Clamp(shift, areaLow - low, areaHigh - high);
                }

                bool Valid(double scale, Placement placement)
                {
                    var shift = ShiftFor(scale, placement);
                    var positions = horizontal
                        ? Positions(scale, shift, ScaleY, shiftY)
                        : Positions(ScaleX, shiftX, scale, shift);
                    return context.MeetsEntryFloors(positions, entry, floor) && Unobstructed(positions);
                }

                // The stretched extent is at least (scale - 1) * reach, so this
                // upper bound never fits; the extent is convex in the scale.
                var step = tolerance / reach;
                var fits = 1.0;
                var wide = 1 + (areaHigh - areaLow) / reach;
                while (wide - fits > step)
                {
                    var probe = (fits + wide) / 2;
                    if (Extent(probe) != null)
                        fits = probe;
                    else
                        wide = probe;
                }
                if (fits <= 1 + step)
                    return true;

                var bestScale = 1.0;
                var bestPlacement = Placement.Center;
                foreach (var placement in new[] { Placement.Center, Placement.Low, Placement.High })
                {
                    if (token.IsCancellationRequested)
                        return false;
                    if (Valid(fits, placement))
                    {
                        bestScale = fits;
                        bestPlacement = placement;
                        break;
                    }

                    // Scale 1 is the entry layout, which meets every floor.
                    var good = 1.0;
                    var bad = fits;
                    while (bad - good > step)
                    {
                        if (token.IsCancellationRequested)
                            return false;
                        var probe = (good + bad) / 2;
                        if (Valid(probe, placement))
                            good = probe;
                        else
                            bad = probe;
                    }
                    if (good > bestScale)
                    {
                        bestScale = good;
                        bestPlacement = placement;
                    }
                }

                if (bestScale <= 1)
                    return true;
                var bestShift = ShiftFor(bestScale, bestPlacement);
                if (horizontal)
                {
                    ScaleX = bestScale;
                    shiftX = bestShift;
                }
                else
                {
                    ScaleY = bestScale;
                    shiftY = bestShift;
                }
                return true;
            }
        }
    }
}
