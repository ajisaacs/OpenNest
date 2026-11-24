using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.BestFit
{
    public class PairEvaluator : IPairEvaluator
    {
        private const double ChordTolerance = 0.01;

        /// <summary>
        /// Tighter chord tolerance for the overlap check only. Rounded-corner arcs
        /// polygonized at the coarser <see cref="ChordTolerance"/> can "cut the corner"
        /// enough to hide a genuine but tiny sliver overlap between two candidates —
        /// this needs to match the precision Part.Intersects uses elsewhere so BestFit's
        /// Keep decision agrees with the same overlap check callers rely on downstream.
        /// </summary>
        private const double OverlapChordTolerance = 0.001;

        public List<BestFitResult> EvaluateAll(List<PairCandidate> candidates)
        {
            if (candidates.Count == 0)
                return new List<BestFitResult>();

            // Build the perimeter-only geometry once: all candidates share the same drawing.
            // This avoids cloning the full program (with all cutouts) for every candidate, and
            // rebuilding the parts' geometry for every candidate.
            var geometry = new PairGeometry(CreatePerimeterDrawing(candidates[0].Drawing));

            // Results keep candidate order: callers sort stably, so ties must not depend on
            // which worker finished first.
            var results = new BestFitResult[candidates.Count];

            Parallel.For(
                0,
                candidates.Count,
                i =>
                {
                    results[i] = Evaluate(candidates[i], geometry, out _, out _);
                }
            );

            return results.ToList();
        }

        public BestFitResult Evaluate(PairCandidate candidate)
        {
            return Evaluate(
                candidate,
                new PairGeometry(CreatePerimeterDrawing(candidate.Drawing)),
                out _,
                out _
            );
        }

        /// <summary>
        /// The two polygons the overlap check reads for <paramref name="candidate"/>, either
        /// null when the drawing has no perimeter. For tests, which compare them with the
        /// polygons of two parts built, placed and turned the long way.
        /// </summary>
        internal (Polygon First, Polygon Second) OverlapPolygons(PairCandidate candidate)
        {
            Evaluate(
                candidate,
                new PairGeometry(CreatePerimeterDrawing(candidate.Drawing)),
                out var first,
                out var second
            );
            return (first, second);
        }

        /// <summary>
        /// Evaluates one candidate as if both parts were built from the perimeter drawing with
        /// <see cref="Part.CreateAtOrigin"/>, placed and turned for it alone. Whatever depends
        /// only on a rotation comes from <paramref name="geometry"/>; whatever depends on the
        /// candidate's offset is applied with the arithmetic such a part would use, in the same
        /// order, so the result is the same to the last bit.
        /// </summary>
        private static BestFitResult Evaluate(
            PairCandidate candidate,
            PairGeometry geometry,
            out Polygon polygon1,
            out Polygon polygon2
        )
        {
            var part1 = geometry.Part1;
            var part2 = geometry.Turned(candidate.Part2Rotation);
            var location1 = part1.Location;
            var location2 = candidate.Part2Offset;

            var box1 = part1.Bounds;
            var box2 = new Box(
                part2.ProgramBounds.X,
                part2.ProgramBounds.Y,
                part2.ProgramBounds.Length,
                part2.ProgramBounds.Width
            );
            box2.Offset(location2);

            // Convex hull vertices from perimeter polygons only
            var vertices1 = part1.PlacedVertices;
            var vertices2 = part2.Vertices;
            var allPoints = new Vector[vertices1.Length + vertices2.Length];
            vertices1.CopyTo(allPoints, 0);

            for (var i = 0; i < vertices2.Length; i++)
                allPoints[vertices1.Length + i] = vertices2[i].Offset(location2);

            // Find optimal bounding rectangle via rotating calipers
            double bestArea,
                bestWidth,
                bestHeight,
                bestRotation;
            List<double> hullAngles = null;

            if (allPoints.Length >= 3)
            {
                var hull = ConvexHull.Compute(allPoints);
                var result = RotatingCalipers.MinimumBoundingRectangle(hull);
                bestArea = result.Area;
                bestWidth = result.Width;
                bestHeight = result.Height;
                bestRotation = result.Angle;
                hullAngles = RotationAnalysis.GetHullEdgeAngles(hull);
            }
            else
            {
                var combinedBox = PairBounds(box1, box2);
                bestArea = combinedBox.Area();
                bestWidth = combinedBox.Width;
                bestHeight = combinedBox.Length;
                bestRotation = 0;
                hullAngles = new List<double> { 0 };
            }

            var trueArea = candidate.Drawing.Area * 2;

            // Normalize to landscape (width >= height) for consistent display. Do this before
            // the overlap check so bestRotation already matches the final OptimalRotation that
            // BuildParts will apply.
            if (bestHeight > bestWidth)
            {
                var tmp = bestWidth;
                bestWidth = bestHeight;
                bestHeight = tmp;
                bestRotation += Angle.HalfPI;
            }

            // Overlap check — perimeter vs perimeter, in the same final orientation BuildParts
            // uses downstream. Uses Collision.HasOverlap (full polygon clip) rather than
            // Shape.Intersects (edge-crossing only), which misses containment-style overlaps
            // where one perimeter's boundary never crosses the other's. Checking pre-rotation
            // geometry here (rather than turning both parts first, matching BuildParts) would
            // tessellate arcs at a different orientation than the geometry actually gets placed
            // with, letting tangent-corner slivers slip through in one frame but not the other.
            var turned = !bestRotation.IsEqualTo(0);

            if (turned)
            {
                var center = PairBounds(box1, box2).Center;
                location1 = location1.Rotate(-bestRotation, center);
                location2 = location2.Rotate(-bestRotation, center);
            }

            polygon1 = part1.OverlapPolygon(turned, -bestRotation, location1);
            polygon2 = part2.OverlapPolygon(turned, -bestRotation, location2);
            var overlaps = Overlap(polygon1, polygon2);

            return new BestFitResult
            {
                Candidate = candidate,
                RotatedArea = bestArea,
                BoundingWidth = bestWidth,
                BoundingHeight = bestHeight,
                OptimalRotation = bestRotation,
                TrueArea = trueArea,
                HullAngles = hullAngles,
                Keep = !overlaps,
                Reason = overlaps ? "Overlap detected" : "Valid",
            };
        }

        /// <summary>The box around both parts, as <c>GetBoundingBox</c> gives it for the pair.</summary>
        private static Box PairBounds(Box first, Box second)
        {
            var left = first.Left;
            var bottom = first.Bottom;
            var right = first.Right;
            var top = first.Top;

            if (second.Left < left)
                left = second.Left;
            if (second.Right > right)
                right = second.Right;
            if (second.Bottom < bottom)
                bottom = second.Bottom;
            if (second.Top > top)
                top = second.Top;

            return new Box(left, bottom, right - left, top - bottom);
        }

        [System.ThreadStatic]
        private static List<CollisionTriangle> firstTriangles;

        [System.ThreadStatic]
        private static List<CollisionTriangle> secondTriangles;

        /// <summary>
        /// <see cref="Collision.HasOverlap(Polygon, Polygon, List{Polygon}, List{Polygon})"/> of
        /// two overlap polygons, false when either part has no perimeter. The triangles go into
        /// lists this thread reuses.
        /// </summary>
        private static bool Overlap(Polygon first, Polygon second)
        {
            if (first == null || second == null)
                return false;

            return Collision.HasOverlapTriangulated(
                first,
                () => Collision.TriangulateForClipping(first, firstTriangles ??= new List<CollisionTriangle>()),
                second,
                () => Collision.TriangulateForClipping(second, secondTriangles ??= new List<CollisionTriangle>())
            );
        }

        /// <summary>
        /// The overlap polygon of a perimeter this candidate owns, moved to
        /// <paramref name="location"/>. The perimeter is moved before it is flattened, as a
        /// placed part's is: flattening first and moving the points afterwards rounds
        /// differently.
        /// </summary>
        private static Polygon PlacedPolygon(Shape perimeter, Vector location)
        {
            if (perimeter == null)
                return null;

            perimeter.Offset(location);
            return perimeter.ToPolygonWithTolerance(OverlapChordTolerance);
        }

        /// <summary>A copy of a shared perimeter for one candidate to move, or null for none.</summary>
        private static Shape Copy(Shape perimeter)
        {
            if (perimeter == null)
                return null;

            var copy = new Shape();

            foreach (var entity in perimeter.Entities)
                copy.Entities.Add(entity.Clone());

            return copy;
        }

        private static Drawing CreatePerimeterDrawing(Drawing source)
        {
            var entities = ConvertProgram
                .ToGeometry(source.Program)
                .Where(e => SpecialLayers.IsMaterial(e.Layer))
                .ToList();
            var profile = new ShapeProfile(entities);
            var program = ConvertGeometry.ToProgram(profile.Perimeter);
            return new Drawing(source.Name, program);
        }

        /// <summary>
        /// The perimeter drawing's geometry for one evaluation run: the first part, and the
        /// second part at each rotation the candidates use. Entries are built on first use and
        /// only read afterwards, so the workers share them.
        /// </summary>
        private sealed class PairGeometry
        {
            private readonly Drawing perimeterDrawing;
            private readonly ConcurrentDictionary<long, TurnedPart> turned = new();

            private readonly PerimeterChain chain;

            public PairGeometry(Drawing perimeterDrawing)
            {
                this.perimeterDrawing = perimeterDrawing;
                chain = PerimeterChain.Record(perimeterDrawing.Program);
                Part1 = new TurnedPart(perimeterDrawing, 0, chain);
            }

            public TurnedPart Part1 { get; }

            /// <summary>The second part at <paramref name="rotation"/>, keyed by its exact value.</summary>
            public TurnedPart Turned(double rotation) =>
                turned.GetOrAdd(
                    System.BitConverter.DoubleToInt64Bits(rotation),
                    static (_, state) =>
                        new TurnedPart(state.perimeterDrawing, state.rotation, state.chain),
                    (perimeterDrawing, rotation, chain)
                );
        }

        /// <summary>
        /// How <see cref="ShapeBuilder.GetShapes"/> chains the perimeter program's entities into
        /// its first shape: which entities, in which order, and which it reverses. Chaining
        /// asks only whether two entity ends lie within <see cref="Tolerance.ChainTolerance"/>,
        /// and turning the program moves every end rigidly, apart from rounding and an arc's
        /// start, which may sit up to <see cref="Tolerance.Epsilon"/> off the previous move's
        /// end depending on whether the conversion refits its center. A record is therefore
        /// kept only when every pair of ends is clearly together or clearly apart, by more
        /// than those shifts can add up to; the chain is then the same at every rotation and is
        /// replayed instead of searched for. Otherwise there is no record and every perimeter
        /// is chained by <see cref="ShapeBuilder.GetShapes"/> itself.
        /// </summary>
        private sealed class PerimeterChain
        {
            /// <summary>Ends this close stay within the chain tolerance at any rotation.</summary>
            private const double Together = 2 * Tolerance.Epsilon;

            /// <summary>Ends this far apart stay outside the chain tolerance at any rotation.</summary>
            private const double Apart = 2 * Tolerance.ChainTolerance;

            private readonly EntityType[] types;
            private readonly int[] order;
            private readonly bool[] reversed;

            private PerimeterChain(EntityType[] types, int[] order, bool[] reversed)
            {
                this.types = types;
                this.order = order;
                this.reversed = reversed;
            }

            /// <summary>The chain of <paramref name="program"/>'s perimeter, or null when it is ambiguous.</summary>
            public static PerimeterChain Record(Program program)
            {
                var entities = MaterialEntities(program);
                var types = new EntityType[entities.Count];
                var starts = new Vector[entities.Count];
                var ends = new List<Vector>(entities.Count * 2);

                for (var i = 0; i < entities.Count; i++)
                {
                    types[i] = entities[i].Type;

                    switch (entities[i])
                    {
                        case Line line:
                            starts[i] = line.StartPoint;
                            ends.Add(line.StartPoint);
                            ends.Add(line.EndPoint);
                            break;

                        case Arc arc:
                            starts[i] = arc.StartPoint();
                            ends.Add(starts[i]);
                            ends.Add(arc.EndPoint());
                            break;

                        case Circle:
                            break;

                        default:
                            return null;
                    }
                }

                if (!ClearlySeparated(ends))
                    return null;

                var positions = new Dictionary<Entity, int>(ReferenceEqualityComparer.Instance);

                for (var i = 0; i < entities.Count; i++)
                    positions[entities[i]] = i;

                var shapes = ShapeBuilder.GetShapes(entities);

                if (shapes.Count == 0)
                    return new PerimeterChain(types, System.Array.Empty<int>(), System.Array.Empty<bool>());

                var chained = shapes[0].Entities;
                var order = new int[chained.Count];
                var reversed = new bool[chained.Count];

                for (var i = 0; i < chained.Count; i++)
                {
                    if (!positions.TryGetValue(chained[i], out var position))
                        return null;

                    // Chaining reverses an entity in place, which swaps its ends.
                    var start = chained[i] switch
                    {
                        Line line => line.StartPoint,
                        Arc arc => arc.StartPoint(),
                        _ => starts[position],
                    };

                    order[i] = position;
                    reversed[i] = start.X != starts[position].X || start.Y != starts[position].Y;
                }

                return new PerimeterChain(types, order, reversed);
            }

            /// <summary>How many material entities the program has.</summary>
            public int EntityCount => types.Length;

            /// <summary>How many entities the perimeter chains, none when there is no perimeter.</summary>
            public int Length => order.Length;

            /// <summary>The kind of the material entity at <paramref name="index"/>.</summary>
            public EntityType TypeOf(int index) => types[index];

            /// <summary>The material entity the perimeter takes at <paramref name="step"/>.</summary>
            public int EntityAt(int step) => order[step];

            /// <summary>Whether chaining reverses the entity taken at <paramref name="step"/>.</summary>
            public bool ReversedAt(int step) => reversed[step];

            /// <summary>
            /// Chains <paramref name="entities"/>, the material entities of the same program at
            /// another rotation, as recorded. False, with nothing changed, when they are not the
            /// same kinds of entity: an arc can become a full circle, or the reverse, when its
            /// end angles round together.
            /// </summary>
            public bool TryReplay(List<Entity> entities, out Shape perimeter)
            {
                perimeter = null;

                if (entities.Count != types.Length)
                    return false;

                for (var i = 0; i < types.Length; i++)
                {
                    if (entities[i].Type != types[i])
                        return false;
                }

                if (order.Length == 0)
                    return true;

                perimeter = new Shape();

                for (var i = 0; i < order.Length; i++)
                {
                    var entity = entities[order[i]];

                    if (reversed[i])
                        entity.Reverse();

                    perimeter.Entities.Add(entity);
                }

                return true;
            }

            /// <summary>
            /// Whether every two ends are either within <see cref="Together"/> or at least
            /// <see cref="Apart"/> from each other, and all are finite.
            /// </summary>
            private static bool ClearlySeparated(List<Vector> ends)
            {
                foreach (var end in ends)
                {
                    if (!double.IsFinite(end.X) || !double.IsFinite(end.Y))
                        return false;
                }

                ends.Sort((a, b) => a.X.CompareTo(b.X));

                for (var i = 0; i < ends.Count; i++)
                {
                    for (var j = i + 1; j < ends.Count && ends[j].X - ends[i].X < Apart; j++)
                    {
                        var distance = ends[i].DistanceTo(ends[j]);

                        if (distance > Together && distance < Apart)
                            return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// One part of the pair as <see cref="Part.CreateAtOrigin"/> builds it at a rotation,
        /// before the candidate's offset: its bounds, hull vertices and perimeter, and the
        /// perimeter after a further turn into each bounding-rectangle orientation.
        /// </summary>
        private sealed class TurnedPart
        {
            private readonly Program program;
            private readonly PerimeterChain chain;
            private readonly PerimeterCodes codes;

            public TurnedPart(Drawing perimeterDrawing, double rotation, PerimeterChain chain)
            {
                var part = Part.CreateAtOrigin(perimeterDrawing, rotation);

                this.chain = chain;
                program = part.Program;
                codes = PerimeterCodes.Read(program, chain);
                Location = part.Location;
                Bounds = part.BoundingBox;
                ProgramBounds = program.BoundingBox();

                var shapes = ShapeBuilder.GetShapes(MaterialEntities(program));
                var points = new List<Vector>();

                foreach (var shape in shapes)
                    points.AddRange(shape.ToPolygonWithTolerance(ChordTolerance).Vertices);

                Perimeter = shapes.Count == 0 ? null : shapes[0];
                Vertices = points.ToArray();
                PlacedVertices = new Vector[Vertices.Length];

                for (var i = 0; i < Vertices.Length; i++)
                    PlacedVertices[i] = Vertices[i].Offset(Location);
            }

            /// <summary>Where <see cref="Part.CreateAtOrigin"/> leaves the part.</summary>
            public Vector Location { get; }

            /// <summary>The part's bounds at <see cref="Location"/>. Read only.</summary>
            public Box Bounds { get; }

            /// <summary>The program's bounds, before any location is added. Read only.</summary>
            public Box ProgramBounds { get; }

            /// <summary>Hull vertices of the program, before any location is added.</summary>
            public Vector[] Vertices { get; }

            /// <summary><see cref="Vertices"/> moved to <see cref="Location"/>.</summary>
            public Vector[] PlacedVertices { get; }

            /// <summary>The program's perimeter, or null when it has none. Read only.</summary>
            public Shape Perimeter { get; }

            /// <summary>
            /// The polygon the overlap check reads for this part at <paramref name="location"/>,
            /// after the program turns by <paramref name="angle"/> when
            /// <paramref name="turned"/>; null when the part has no perimeter. It is flattened
            /// straight from the program's codes where they allow it, and otherwise from the
            /// perimeter shape a part would build.
            /// </summary>
            public Polygon OverlapPolygon(bool turned, double angle, Vector location)
            {
                if (codes != null && codes.TryFlatten(turned, angle, location, out var polygon))
                    return polygon;

                return PlacedPolygon(turned ? PerimeterTurned(angle) : Copy(Perimeter), location);
            }

            /// <summary>
            /// The perimeter after the program turns by <paramref name="angle"/>, or null when
            /// it has none. Bounding-rectangle angles rarely repeat between candidates, so this
            /// is built per call, and the caller owns the result.
            /// </summary>
            private Shape PerimeterTurned(double angle)
            {
                var copy = (Program)program.Clone();
                copy.Rotate(angle);
                var entities = MaterialEntities(copy);

                if (chain != null && chain.TryReplay(entities, out var perimeter))
                    return perimeter;

                var shapes = ShapeBuilder.GetShapes(entities);
                return shapes.Count == 0 ? null : shapes[0];
            }
        }

        /// <summary>
        /// A perimeter program's moves, read once, from which a candidate's overlap polygon is
        /// flattened without copying the program or building its entities. Each step is the one
        /// the long way takes, with the same operands in the same order: turning the codes
        /// (<see cref="Program.Rotate(double)"/>), converting them
        /// (<see cref="ConvertProgram.ToGeometry"/>), chaining and reversing them (the
        /// <see cref="PerimeterChain"/>), moving them to the part's location and flattening
        /// them (<see cref="Shape.ToPolygonWithTolerance"/>). Arcs are fitted and flattened by
        /// the conversion's and <see cref="Arc"/>'s own helpers.
        /// </summary>
        private sealed class PerimeterCodes
        {
            private readonly PerimeterChain chain;
            private readonly bool[] isArc;
            private readonly bool[] clockwise;
            private readonly Vector[] ends;
            private readonly Vector[] centers;

            /// <summary>Each move's place among the material entities, or -1 when it is not material.</summary>
            private readonly int[] slots;

            private int vertexCount = 64;

            [System.ThreadStatic]
            private static Piece[] pieceBuffer;

            private PerimeterCodes(
                PerimeterChain chain,
                bool[] isArc,
                bool[] clockwise,
                Vector[] ends,
                Vector[] centers,
                int[] slots
            )
            {
                this.chain = chain;
                this.isArc = isArc;
                this.clockwise = clockwise;
                this.ends = ends;
                this.centers = centers;
                this.slots = slots;
            }

            /// <summary>
            /// The moves of <paramref name="program"/>, or null when it must go the long way:
            /// no recorded chain, no perimeter, incremental moves, sub-programs, a full circle,
            /// or moves that do not match the chain's entities one for one.
            /// </summary>
            public static PerimeterCodes Read(Program program, PerimeterChain chain)
            {
                if (chain == null || chain.Length == 0 || program.Mode != Mode.Absolute)
                    return null;

                var isArc = new List<bool>();
                var clockwise = new List<bool>();
                var ends = new List<Vector>();
                var centers = new List<Vector>();

                for (var i = 0; i < program.Length; i++)
                {
                    var code = program[i];

                    switch (code.Type)
                    {
                        case CodeType.ArcMove:
                            var arc = (ArcMove)code;
                            isArc.Add(true);
                            clockwise.Add(arc.Rotation == RotationType.CW);
                            ends.Add(arc.EndPoint);
                            centers.Add(arc.CenterPoint);
                            break;

                        case CodeType.LinearMove:
                        case CodeType.RapidMove:
                            isArc.Add(false);
                            clockwise.Add(false);
                            ends.Add(((Motion)code).EndPoint);
                            centers.Add(new Vector());
                            break;

                        case CodeType.SubProgramCall:
                            return null;
                    }
                }

                // The conversion gives one entity per move, in order: its layers say which are
                // material, and so which entity of the chain each move is.
                var entities = ConvertProgram.ToGeometry(program);

                if (entities.Count != isArc.Count)
                    return null;

                var slots = new int[entities.Count];
                var material = 0;

                for (var i = 0; i < entities.Count; i++)
                {
                    if (!SpecialLayers.IsMaterial(entities[i].Layer))
                    {
                        slots[i] = -1;
                        continue;
                    }

                    if (material >= chain.EntityCount)
                        return null;

                    if (chain.TypeOf(material) != (isArc[i] ? EntityType.Arc : EntityType.Line))
                        return null;

                    slots[i] = material++;
                }

                if (material != chain.EntityCount)
                    return null;

                return new PerimeterCodes(
                    chain,
                    isArc.ToArray(),
                    clockwise.ToArray(),
                    ends.ToArray(),
                    centers.ToArray(),
                    slots
                );
            }

            /// <summary>
            /// Flattens the perimeter at <paramref name="location"/>, after the program turns by
            /// <paramref name="angle"/> when <paramref name="turned"/>. False when an arc comes
            /// out as a full circle at this angle, which the chain did not record.
            /// </summary>
            public bool TryFlatten(bool turned, double angle, Vector location, out Polygon polygon)
            {
                polygon = null;

                var pieces = pieceBuffer;

                if (pieces == null || pieces.Length < chain.EntityCount)
                    pieceBuffer = pieces = new Piece[chain.EntityCount];

                // Program.Rotate turns every move about the origin; Vector.Rotate takes the
                // cosine and sine of the same angle for each point.
                var origin = new Vector(0, 0);
                var cos = turned ? PortableMath.Cos(angle) : 0;
                var sin = turned ? PortableMath.Sin(angle) : 0;

                // ConvertProgram.ToGeometry: each move runs from where the last one ended.
                var current = new Vector();

                for (var i = 0; i < ends.Length; i++)
                {
                    var end = turned ? Turn(ends[i], cos, sin, origin) : ends[i];
                    var slot = slots[i];

                    if (!isArc[i])
                    {
                        if (slot >= 0)
                            pieces[slot] = new Piece { Start = current, End = end };
                    }
                    else
                    {
                        var center = turned ? Turn(centers[i], cos, sin, origin) : centers[i];
                        center = ConvertProgram.FitCenterToEndpoints(center, current, end);

                        var startAngle = center.AngleTo(current);
                        var endAngle = center.AngleTo(end);

                        var dx = end.X - center.X;
                        var dy = end.Y - center.Y;
                        var radius = System.Math.Sqrt(dx * dx + dy * dy);

                        if (startAngle.IsEqualTo(endAngle))
                            return false;

                        if (slot >= 0)
                        {
                            pieces[slot] = new Piece
                            {
                                IsArc = true,
                                Start = center,
                                Radius = radius,
                                StartAngle = startAngle,
                                EndAngle = endAngle,
                                Reversed = clockwise[i],
                            };
                        }
                    }

                    current = end;
                }

                polygon = new Polygon();
                var vertices = polygon.Vertices;
                vertices.Capacity = vertexCount;

                for (var step = 0; step < chain.Length; step++)
                {
                    ref readonly var piece = ref pieces[chain.EntityAt(step)];
                    var reverse = chain.ReversedAt(step);

                    if (!piece.IsArc)
                    {
                        // Line.Reverse swaps the ends; Line.Offset adds the location to each.
                        vertices.Add((reverse ? piece.End : piece.Start) + location);
                        vertices.Add((reverse ? piece.Start : piece.End) + location);
                        continue;
                    }

                    // Arc.Reverse swaps the angles and flips the direction; Arc.Offset adds
                    // the location to the center.
                    var startAngle = reverse ? piece.EndAngle : piece.StartAngle;
                    var endAngle = reverse ? piece.StartAngle : piece.EndAngle;
                    var reversed = piece.Reversed != reverse;
                    var sweep = Arc.SweepAngle(startAngle, endAngle, reversed);

                    Arc.AppendPoints(
                        vertices,
                        piece.Start + location,
                        piece.Radius,
                        startAngle,
                        sweep,
                        reversed,
                        Arc.SegmentsForTolerance(piece.Radius, sweep, OverlapChordTolerance),
                        circumscribe: false
                    );
                }

                // Sized for the next candidate: the count barely changes with the angle.
                if (vertices.Count + 16 > vertexCount)
                    vertexCount = vertices.Count + 16;

                polygon.Close();
                polygon.Cleanup();
                polygon.UpdateBounds();
                return true;
            }

            /// <summary><see cref="Vector.Rotate(double, Vector)"/> with the angle's cosine and sine given.</summary>
            private static Vector Turn(Vector point, double cos, double sin, Vector origin)
            {
                var pt = point - origin;

                return new Vector(
                    pt.X * cos - pt.Y * sin + origin.X,
                    pt.X * sin + pt.Y * cos + origin.Y
                );
            }

            /// <summary>One material entity: a line from Start to End, or an arc centered on Start.</summary>
            private struct Piece
            {
                public bool IsArc;
                public Vector Start;
                public Vector End;
                public double Radius;
                public double StartAngle;
                public double EndAngle;
                public bool Reversed;
            }
        }

        private static List<Entity> MaterialEntities(Program program) =>
            ConvertProgram
                .ToGeometry(program)
                .Where(e => SpecialLayers.IsMaterial(e.Layer))
                .ToList();
    }
}
