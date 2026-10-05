using System;
using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.CNC.CuttingStrategy
{
    public class ContourCuttingStrategy
    {
        public CuttingParameters Parameters { get; set; }

        private record ContourEntry(Shape Shape, Vector Point, Entity Entity);

        public CuttingResult Apply(Program partProgram, Vector approachPoint)
        {
            return Apply(partProgram, approachPoint, Vector.Invalid);
        }

        public CuttingResult Apply(Program partProgram, Vector approachPoint, Vector nextPartStart)
        {
            var entities = partProgram.ToGeometry();
            entities.RemoveAll(e => e.Layer == SpecialLayers.Rapid);

            var scribeEntities = entities.FindAll(e => e.Layer == SpecialLayers.Scribe);
            entities.RemoveAll(e => e.Layer == SpecialLayers.Scribe);

            var profile = new ShapeProfile(entities);

            // Start from the bounding box corner opposite the origin (max X, max Y)
            var bbox = entities.GetBoundingBox();
            var startCorner = new Vector(bbox.Right, bbox.Top);

            // Initial pass: sequence cutouts from bbox corner
            var seedPoint = startCorner;
            var orderedCutouts = SequenceCutouts(profile.Cutouts, seedPoint);
            orderedCutouts.Reverse();

            var perimeterSeed = profile.Perimeter.ClosestPointTo(seedPoint, out _);
            var cutoutEntries = ResolveLeadInPoints(orderedCutouts, perimeterSeed);

            Vector perimeterPt;
            Entity perimeterEntity;

            if (!double.IsNaN(nextPartStart.X) && cutoutEntries.Count > 0)
            {
                // Iterate: each pass refines the perimeter lead-in which changes
                // the internal sequence which changes the last cutout position
                for (var iter = 0; iter < 3; iter++)
                {
                    var lastCutoutPt = cutoutEntries[cutoutEntries.Count - 1].Point;
                    perimeterSeed = FindPerimeterIntersection(
                        profile.Perimeter,
                        lastCutoutPt,
                        nextPartStart,
                        out _
                    );

                    orderedCutouts = SequenceCutouts(profile.Cutouts, perimeterSeed);
                    orderedCutouts.Reverse();
                    cutoutEntries = ResolveLeadInPoints(orderedCutouts, perimeterSeed);
                }

                var finalLastCutout = cutoutEntries[cutoutEntries.Count - 1].Point;
                perimeterPt = FindPerimeterIntersection(
                    profile.Perimeter,
                    finalLastCutout,
                    nextPartStart,
                    out perimeterEntity
                );
            }
            else
            {
                var perimeterRef = cutoutEntries.Count > 0 ? cutoutEntries[0].Point : approachPoint;
                perimeterPt = profile.Perimeter.ClosestPointTo(perimeterRef, out perimeterEntity);
            }

            var result = new Program(Mode.Absolute);

            EmitScribeContours(result, scribeEntities);

            foreach (var entry in cutoutEntries)
            {
                if (!entry.Shape.IsClosed())
                    EmitRawContour(result, entry.Shape);
                else
                    EmitContour(result, entry.Shape, entry.Point, entry.Entity);
            }

            if (!profile.Perimeter.IsClosed())
                EmitRawContour(result, profile.Perimeter);
            else
                EmitContour(
                    result,
                    profile.Perimeter,
                    perimeterPt,
                    perimeterEntity,
                    ContourType.External
                );

            result.Mode = Mode.Incremental;

            return new CuttingResult { Program = result, LastCutPoint = perimeterPt };
        }

        public CuttingResult ApplySingle(
            Program partProgram,
            Vector point,
            Entity entity,
            ContourType contourType
        )
        {
            var entities = partProgram.ToGeometry();
            entities.RemoveAll(e => e.Layer == SpecialLayers.Rapid);

            var scribeEntities = entities.FindAll(e => e.Layer == SpecialLayers.Scribe);
            entities.RemoveAll(e => e.Layer == SpecialLayers.Scribe);

            var profile = new ShapeProfile(entities);

            var result = new Program(Mode.Absolute);

            EmitScribeContours(result, scribeEntities);

            // Find the target shape that contains the clicked entity
            var (targetShape, matchedEntity) = FindTargetShape(profile, point, entity);

            // Emit cutouts — only the target gets lead-in/out (skip open contours)
            foreach (var cutout in profile.Cutouts)
            {
                if (!cutout.IsClosed())
                {
                    EmitRawContour(result, cutout);
                }
                else if (cutout == targetShape)
                {
                    var ct = DetectContourType(cutout);
                    EmitContour(result, cutout, point, matchedEntity, ct);
                }
                else
                {
                    EmitRawContour(result, cutout);
                }
            }

            // Emit perimeter
            if (!profile.Perimeter.IsClosed())
            {
                EmitRawContour(result, profile.Perimeter);
            }
            else if (profile.Perimeter == targetShape)
            {
                EmitContour(result, profile.Perimeter, point, matchedEntity, ContourType.External);
            }
            else
            {
                EmitRawContour(result, profile.Perimeter);
            }

            result.Mode = Mode.Incremental;

            return new CuttingResult { Program = result, LastCutPoint = point };
        }

        private static (Shape Shape, Entity Entity) FindTargetShape(
            ShapeProfile profile,
            Vector point,
            Entity clickedEntity
        )
        {
            var matched = FindMatchingEntity(profile.Perimeter, clickedEntity);
            if (matched != null)
                return (profile.Perimeter, matched);

            foreach (var cutout in profile.Cutouts)
            {
                matched = FindMatchingEntity(cutout, clickedEntity);
                if (matched != null)
                    return (cutout, matched);
            }

            // Fallback: closest shape, use closest point to find entity
            var best = profile.Perimeter;
            var bestPt = profile.Perimeter.ClosestPointTo(point, out var bestEntity);
            var bestDist = bestPt.DistanceTo(point);

            foreach (var cutout in profile.Cutouts)
            {
                var pt = cutout.ClosestPointTo(point, out var cutoutEntity);
                var dist = pt.DistanceTo(point);
                if (dist < bestDist)
                {
                    best = cutout;
                    bestEntity = cutoutEntity;
                    bestDist = dist;
                }
            }

            return (best, bestEntity);
        }

        private static Entity FindMatchingEntity(Shape shape, Entity clickedEntity)
        {
            foreach (var shapeEntity in shape.Entities)
            {
                if (shapeEntity.GetType() != clickedEntity.GetType())
                    continue;

                if (shapeEntity is Line sLine && clickedEntity is Line cLine)
                {
                    if (
                        sLine.StartPoint.DistanceTo(cLine.StartPoint) < Math.Tolerance.Epsilon
                        && sLine.EndPoint.DistanceTo(cLine.EndPoint) < Math.Tolerance.Epsilon
                    )
                        return shapeEntity;
                }
                else if (shapeEntity is Arc sArc && clickedEntity is Arc cArc)
                {
                    if (
                        System.Math.Abs(sArc.Radius - cArc.Radius) < Math.Tolerance.Epsilon
                        && sArc.Center.DistanceTo(cArc.Center) < Math.Tolerance.Epsilon
                    )
                        return shapeEntity;
                }
                else if (shapeEntity is Circle sCircle && clickedEntity is Circle cCircle)
                {
                    if (
                        System.Math.Abs(sCircle.Radius - cCircle.Radius) < Math.Tolerance.Epsilon
                        && sCircle.Center.DistanceTo(cCircle.Center) < Math.Tolerance.Epsilon
                    )
                        return shapeEntity;
                }
            }

            return null;
        }

        internal Program EmitPrepared(Shape[] shapes, List<Entity> scribes,
            IReadOnlyList<CuttingPlanning.ContourChoice> choices)
        {
            var result = new Program(Mode.Absolute);
            EmitScribeContours(result, scribes);
            foreach (var choice in choices)
            {
                var shape = shapes[choice.ContourOrdinal];
                EmitContour(result, shape, choice.Point, shape.Entities[choice.EntityOrdinal],
                    choice.ContourOrdinal == shapes.Length - 1 ? ContourType.External : null,
                    exactCirclePrograms: true);
            }
            result.Mode = Mode.Incremental;
            return result;
        }

        private void EmitRawContour(Program program, Shape shape)
        {
            var startPoint = GetShapeStartPoint(shape);
            program.Codes.Add(new RapidMove(startPoint));
            program.Codes.AddRange(ConvertShapeToMoves(shape, startPoint));
        }

        private static List<ContourEntry> ResolveLeadInPoints(
            List<Shape> cutouts,
            Vector startPoint
        )
        {
            var entries = new ContourEntry[cutouts.Count];
            var currentPoint = startPoint;

            // Walk backward through cutting order (from perimeter outward)
            // so each cutout's lead-in point faces the next cutout to be cut
            for (var i = cutouts.Count - 1; i >= 0; i--)
            {
                var closestPt = cutouts[i].ClosestPointTo(currentPoint, out var entity);
                entries[i] = new ContourEntry(cutouts[i], closestPt, entity);
                currentPoint = closestPt;
            }

            return new List<ContourEntry>(entries);
        }

        private static Vector FindPerimeterIntersection(
            Shape perimeter,
            Vector lastCutout,
            Vector nextPartStart,
            out Entity entity
        )
        {
            var ray = new Line(lastCutout, nextPartStart);

            if (perimeter.Intersects(ray, out var pts) && pts.Count > 0)
            {
                // Pick the intersection closest to the last cutout
                var best = pts[0];
                var bestDist = best.DistanceTo(lastCutout);

                for (var i = 1; i < pts.Count; i++)
                {
                    var dist = pts[i].DistanceTo(lastCutout);
                    if (dist < bestDist)
                    {
                        best = pts[i];
                        bestDist = dist;
                    }
                }

                return perimeter.ClosestPointTo(best, out entity);
            }

            // Fallback: closest point on perimeter to the last cutout
            return perimeter.ClosestPointTo(lastCutout, out entity);
        }

        private static int ComputeSubProgramKey(double radius, double normalAngle)
        {
            var r = System.Math.Round(radius, 6);
            var a = System.Math.Round(normalAngle, 6);
            return HashCode.Combine(r, a);
        }

        // Prepared emission compares the actual resolved, generated motions, not a
        // rounded geometry hash. Labels are deterministic encounter-order identifiers;
        // legacy Apply/ApplySingle retain their existing cache and labels unchanged.
        private static int RegisterPreparedCircleProgram(Program owner, Program generated)
        {
            CuttingPlanning.ExecutionMotionReader.ReadSupported(generated, Vector.Zero, null);
            if (generated.Variables.Count != 0 || generated.SubPrograms.Count != 0)
                throw new NotSupportedException("Unsupported generated circle program metadata.");
            foreach (var code in generated.Codes)
                if (code is not (RapidMove or LinearMove or ArcMove) || ((Motion)code).VariableRefs != null)
                    throw new NotSupportedException("Unsupported generated circle instruction.");
            foreach (var pair in owner.SubPrograms)
                if (SameGeneratedCircleProgram(pair.Value, generated))
                    return pair.Key;
            var key = checked(owner.SubPrograms.Count + 1);
            owner.SubPrograms.Add(key, generated);
            return key;
        }

        private static bool SameGeneratedCircleProgram(Program a, Program b)
        {
            if (a.Mode != b.Mode || !SameBits(a.Rotation, b.Rotation) || a.Codes.Count != b.Codes.Count)
                return false;
            for (var i = 0; i < a.Codes.Count; i++)
            {
                var left = (Motion)a.Codes[i];
                var right = (Motion)b.Codes[i];
                if (left.GetType() != right.GetType() || left.Suppressed != right.Suppressed
                    || left.UseExactStop != right.UseExactStop || left.Feedrate != right.Feedrate
                    || !SameVector(left.EndPoint, right.EndPoint))
                    return false;
                if (left is LinearMove line && line.Layer != ((LinearMove)right).Layer)
                    return false;
                if (left is ArcMove arc && (arc.Layer != ((ArcMove)right).Layer
                    || arc.Rotation != ((ArcMove)right).Rotation
                    || !SameVector(arc.CenterPoint, ((ArcMove)right).CenterPoint)))
                    return false;
            }
            return true;
        }

        private static bool SameVector(Vector a, Vector b) => SameBits(a.X, b.X) && SameBits(a.Y, b.Y);
        private static bool SameBits(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

        private void EmitContour(
            Program program,
            Shape shape,
            Vector point,
            Entity entity,
            ContourType? forceType = null,
            bool exactCirclePrograms = false
        )
        {
            var contourType = forceType ?? DetectContourType(shape);
            var winding = DetermineWinding(shape);
            var normal = ComputeNormal(point, entity, contourType, winding);

            var leadIn = SelectLeadIn(contourType);
            var leadOut = SelectLeadOut(contourType);

            if (contourType == ContourType.ArcCircle && entity is Circle circle)
            {
                if (Parameters.RoundLeadInAngles && Parameters.LeadInAngleIncrement > 0)
                {
                    var increment = Angle.ToRadians(Parameters.LeadInAngleIncrement);
                    normal = System.Math.Round(normal / increment) * increment;
                    normal = Angle.NormalizeRad(normal);

                    var outwardAngle = normal - System.Math.PI;
                    point = new Vector(
                        circle.Center.X + circle.Radius * System.Math.Cos(outwardAngle),
                        circle.Center.Y + circle.Radius * System.Math.Sin(outwardAngle)
                    );
                }

                leadIn = ClampLeadInForCircle(leadIn, circle, point, normal);

                // Build hole sub-program relative to (0,0)
                var holeCenter = circle.Center;
                var relativePoint = new Vector(point.X - holeCenter.X, point.Y - holeCenter.Y);
                var relativeCircle = new Circle(new Vector(0, 0), circle.Radius)
                {
                    Rotation = circle.Rotation,
                };
                var relativeShape = new Shape();
                relativeShape.Entities.Add(relativeCircle);

                var subPgm = new Program(Mode.Absolute);
                subPgm.Codes.AddRange(leadIn.Generate(relativePoint, normal, winding));
                var reindexed = relativeShape.ReindexAt(relativePoint, relativeCircle);

                subPgm.Codes.AddRange(ConvertShapeToMoves(reindexed, relativePoint));
                subPgm.Codes.AddRange(leadOut.Generate(relativePoint, normal, winding));
                subPgm.Mode = Mode.Incremental;

                // Deduplicate: check if an identical sub-program already exists
                var key = exactCirclePrograms
                    ? RegisterPreparedCircleProgram(program, subPgm)
                    : ComputeSubProgramKey(circle.Radius, normal);
                if (!program.SubPrograms.ContainsKey(key))
                    program.SubPrograms[key] = subPgm;

                program.Codes.Add(
                    new SubProgramCall
                    {
                        Id = key,
                        Program = program.SubPrograms[key],
                        Offset = holeCenter,
                    }
                );

                return;
            }

            leadIn = ResolveLeadIn(shape, point, entity, contourType, leadIn, winding,
                Parameters.PierceClearance, out var leadInNormal);
            program.Codes.AddRange(leadIn.Generate(point, leadInNormal, winding));

            var reindexedShape = shape.ReindexAt(point, entity);

            var tabbed = Parameters.TabsEnabled
                && Parameters.TabConfig != null
                && contourType == ContourType.External;

            // A tab leaves the contour short of the corner; a run-out through it would cut the tab.
            var leadOutPoint = point;
            var leadOutNormal = normal;
            if (tabbed)
            {
                reindexedShape = TrimShapeForTab(reindexedShape, point, Parameters.TabConfig.Size);

                // Leave from where the trimmed cut actually ends, on that entity's normal:
                // an arc generated at the nominal entry would not start on its own radius.
                if (reindexedShape.Entities.Count > 0 && reindexedShape.Entities[^1] is Line or Arc)
                {
                    var last = reindexedShape.Entities[^1];
                    leadOutPoint = EntityEndPoint(last);
                    leadOutNormal = ComputeNormal(leadOutPoint, last, contourType, winding);
                }
            }
            else
            {
                leadOut = ResolveLeadOut(shape, point, entity, contourType, leadOut, winding,
                    Parameters.PierceClearance, out leadOutNormal);
            }

            program.Codes.AddRange(ConvertShapeToMoves(reindexedShape, point));
            program.Codes.AddRange(leadOut.Generate(leadOutPoint, leadOutNormal, winding));
        }

        private void EmitScribeContours(Program program, List<Entity> scribeEntities)
        {
            if (scribeEntities.Count == 0)
                return;

            var shapes = ShapeBuilder.GetShapes(scribeEntities);
            foreach (var shape in shapes)
            {
                var startPt = GetShapeStartPoint(shape);
                program.Codes.Add(new RapidMove(startPt));
                program.Codes.AddRange(ConvertShapeToMoves(shape, startPt, LayerType.Scribe));
            }
        }

        private List<Shape> SequenceCutouts(List<Shape> cutouts, Vector startPoint)
        {
            var remaining = new List<Shape>(cutouts);
            var ordered = new List<Shape>();
            var currentPoint = startPoint;

            while (remaining.Count > 0)
            {
                var nearest = remaining[0];
                var nearestPt = nearest.ClosestPointTo(currentPoint);
                var nearestDist = nearestPt.DistanceTo(currentPoint);

                for (var i = 1; i < remaining.Count; i++)
                {
                    var pt = remaining[i].ClosestPointTo(currentPoint);
                    var dist = pt.DistanceTo(currentPoint);
                    if (dist < nearestDist)
                    {
                        nearest = remaining[i];
                        nearestPt = pt;
                        nearestDist = dist;
                    }
                }

                ordered.Add(nearest);
                remaining.Remove(nearest);
                currentPoint = nearestPt;
            }

            return ordered;
        }

        public static ContourType DetectContourType(Shape cutout)
        {
            if (cutout.Entities.Count == 1 && cutout.Entities[0] is Circle)
                return ContourType.ArcCircle;

            return ContourType.Internal;
        }

        /// <summary>
        /// Uses the inward angle bisector for straight lead-ins at cutout corners.
        /// Edge interiors and other lead-in styles keep the entity normal. Shared
        /// by program generation and the manual placement preview.
        /// </summary>
        public static double ComputeLeadInNormal(
            Shape shape,
            Vector point,
            Entity entity,
            ContourType contourType,
            LeadIn leadIn,
            RotationType winding = RotationType.CW
        )
        {
            var normal = ComputeNormal(point, entity, contourType, winding);
            if (contourType != ContourType.Internal || leadIn is not LineLeadIn
                || !TryGetCorner(shape, point, entity, out var corner))
                return normal;

            return BisectCorner(point, corner, contourType, winding) ?? normal;
        }

        /// <summary>
        /// Returns the lead-in to emit at <paramref name="point"/> and the normal to
        /// generate it with. At a corner of an outside perimeter, a straight
        /// (<see cref="LineLeadIn"/>) lead-in extends the edge cut first so the torch
        /// enters on that edge's line, provided the pierce keeps
        /// <paramref name="pierceClearance"/> from the contour; the approach angle is
        /// ignored there. Otherwise it is perpendicular to the edge cut first, and at a
        /// reflex corner it bisects the notch. The result does not depend on which of
        /// the two edges meeting at the corner was picked. Other styles and contour
        /// types keep <see cref="ComputeLeadInNormal"/>.
        /// </summary>
        public static LeadIn ResolveLeadIn(
            Shape shape,
            Vector point,
            Entity entity,
            ContourType contourType,
            LeadIn leadIn,
            RotationType winding,
            double pierceClearance,
            out double normal
        )
        {
            normal = ComputeLeadInNormal(shape, point, entity, contourType, leadIn, winding);
            if (contourType != ContourType.External || leadIn is not LineLeadIn line
                || !TryGetCorner(shape, point, entity, out var corner))
                return leadIn;

            switch (ClassifyCorner(corner, winding))
            {
                case CornerKind.Convex:
                    var pierce = point - corner.TangentOut * line.Length;
                    if (IsClearStraightLead(shape, point, pierce, pierceClearance))
                    {
                        normal = Angle.NormalizeRad((-corner.TangentOut).Angle());
                        return new LineLeadIn { Length = line.Length, ApproachAngle = 90 };
                    }
                    normal = ComputeNormal(point, corner.Outgoing, contourType, winding);
                    return leadIn;
                case CornerKind.Smooth:
                    normal = ComputeNormal(point, corner.Outgoing, contourType, winding);
                    return leadIn;
                case CornerKind.Reflex:
                    normal = BisectCorner(point, corner, contourType, winding) ?? normal;
                    return leadIn;
                default:
                    return leadIn;
            }
        }

        /// <summary>
        /// Lead-out counterpart of <see cref="ResolveLeadIn"/>. At a convex outside
        /// perimeter corner a <see cref="LineLeadOut"/> runs straight on past the corner
        /// along the edge cut last, when its end keeps <paramref name="clearance"/> from
        /// the contour; otherwise it is perpendicular to that edge. At a reflex corner it
        /// bisects the notch. Other styles and contour types keep the entity normal.
        /// </summary>
        public static LeadOut ResolveLeadOut(
            Shape shape,
            Vector point,
            Entity entity,
            ContourType contourType,
            LeadOut leadOut,
            RotationType winding,
            double clearance,
            out double normal
        )
        {
            normal = ComputeNormal(point, entity, contourType, winding);
            if (contourType != ContourType.External || leadOut is not LineLeadOut line
                || !TryGetCorner(shape, point, entity, out var corner))
                return leadOut;

            switch (ClassifyCorner(corner, winding))
            {
                case CornerKind.Convex:
                    var end = point + corner.TangentIn * line.Length;
                    if (IsClearStraightLead(shape, point, end, clearance))
                    {
                        normal = Angle.NormalizeRad(corner.TangentIn.Angle());
                        return new LineLeadOut { Length = line.Length, ApproachAngle = 90 };
                    }
                    normal = ComputeNormal(point, corner.Incoming, contourType, winding);
                    return leadOut;
                case CornerKind.Smooth:
                    normal = ComputeNormal(point, corner.Incoming, contourType, winding);
                    return leadOut;
                case CornerKind.Reflex:
                    normal = BisectCorner(point, corner, contourType, winding) ?? normal;
                    return leadOut;
                default:
                    return leadOut;
            }
        }

        private enum CornerKind
        {
            Convex,
            Reflex,
            Smooth,
            Cusp,
        }

        /// <summary>A contour vertex: the entity cut into it and the one cut away from it.</summary>
        private readonly record struct ContourCorner(
            Entity Incoming,
            Entity Outgoing,
            Vector TangentIn,
            Vector TangentOut
        );

        private static bool TryGetCorner(Shape shape, Vector point, Entity entity, out ContourCorner corner)
        {
            corner = default;
            if (entity is not (Line or Arc) || entity.Length <= Tolerance.Epsilon
                || shape.Entities.Count < 2 || !shape.IsClosed())
                return false;

            var index = shape.Entities.IndexOf(entity);
            if (index < 0)
                return false;

            var atStart = point.DistanceTo(EntityStartPoint(entity)) <= Tolerance.Epsilon;
            if (!atStart && point.DistanceTo(EntityEndPoint(entity)) > Tolerance.Epsilon)
                return false;

            var adjacentIndex = atStart
                ? (index + shape.Entities.Count - 1) % shape.Entities.Count
                : (index + 1) % shape.Entities.Count;
            var adjacent = shape.Entities[adjacentIndex];
            var adjacentPoint = atStart ? EntityEndPoint(adjacent) : EntityStartPoint(adjacent);

            if (adjacent is not (Line or Arc) || adjacent.Length <= Tolerance.Epsilon
                || point.DistanceTo(adjacentPoint) > Tolerance.Epsilon)
                return false;

            var incoming = atStart ? adjacent : entity;
            var outgoing = atStart ? entity : adjacent;
            var tangentIn = TravelTangent(incoming, point);
            var tangentOut = TravelTangent(outgoing, point);
            if (!IsFinite(tangentIn) || !IsFinite(tangentOut))
                return false;

            corner = new ContourCorner(incoming, outgoing, tangentIn, tangentOut);
            return true;
        }

        /// <summary>Unit direction of travel along a line or arc at a point on it.</summary>
        private static Vector TravelTangent(Entity entity, Vector point)
        {
            if (entity is Line line)
                return (line.EndPoint - line.StartPoint).Normalize();

            var arc = (Arc)entity;
            var radial = (point - arc.Center).Normalize();
            return arc.IsReversed ? new Vector(radial.Y, -radial.X) : new Vector(-radial.Y, radial.X);
        }

        private static bool IsFinite(Vector v) => double.IsFinite(v.X) && double.IsFinite(v.Y);

        /// <summary>
        /// Convex corners point away from the part (interior angle under 180 degrees).
        /// A turn whose offset over the tangent is within chaining tolerance is smooth,
        /// not a corner.
        /// </summary>
        private static CornerKind ClassifyCorner(ContourCorner corner, RotationType winding)
        {
            var cross = corner.TangentIn.X * corner.TangentOut.Y - corner.TangentIn.Y * corner.TangentOut.X;
            var dot = corner.TangentIn.DotProduct(corner.TangentOut);
            var turn = winding == RotationType.CCW ? cross : -cross;

            if (System.Math.Abs(turn) <= Tolerance.Epsilon)
                return dot > 0 ? CornerKind.Smooth : CornerKind.Cusp;

            return turn > 0 ? CornerKind.Convex : CornerKind.Reflex;
        }

        private static double? BisectCorner(
            Vector point,
            ContourCorner corner,
            ContourType contourType,
            RotationType winding
        )
        {
            var normal = ComputeNormal(point, corner.Outgoing, contourType, winding);
            var adjacentNormal = ComputeNormal(point, corner.Incoming, contourType, winding);
            // Sum unit normals rather than averaging angles (which fails at 0/2π).
            // Winding makes this point into the scrap even at reflex corners.
            var x = System.Math.Cos(normal) + System.Math.Cos(adjacentNormal);
            var y = System.Math.Sin(normal) + System.Math.Sin(adjacentNormal);
            if (!double.IsFinite(x) || !double.IsFinite(y)
                || x * x + y * y <= Tolerance.Epsilon * Tolerance.Epsilon)
                return null; // Opposing normals at a cusp have no unique bisector.

            return Angle.NormalizeRad(System.Math.Atan2(y, x));
        }

        /// <summary>
        /// A straight lead from <paramref name="end"/> to the corner stays in the scrap:
        /// its free end keeps <paramref name="clearance"/> from the contour and the lead
        /// crosses the contour nowhere but at the corner.
        /// </summary>
        private static bool IsClearStraightLead(Shape shape, Vector corner, Vector end, double clearance)
        {
            if (!IsFinite(end) || end.DistanceTo(corner) <= Tolerance.Epsilon)
                return false;

            var nearest = shape.ClosestPointTo(end, out _);
            if (nearest.DistanceTo(end) < System.Math.Max(clearance, 0) - Tolerance.Epsilon)
                return false;

            if (shape.Intersects(new Line(end, corner), out var crossings))
            {
                foreach (var crossing in crossings)
                {
                    if (crossing.DistanceTo(corner) > Tolerance.ChainTolerance)
                        return false;
                }
            }

            return true;
        }

        private static Vector EntityEndPoint(Entity entity)
        {
            if (entity is Line line)
                return line.EndPoint;
            if (entity is Arc arc)
                return arc.EndPoint();
            return Vector.Invalid;
        }

        public static double ComputeNormal(
            Vector point,
            Entity entity,
            ContourType contourType,
            RotationType winding = RotationType.CW
        )
        {
            double normal;

            if (entity is Line line)
            {
                // Perpendicular to line direction: tangent + π/2 = left side.
                // Left side = outward for CW winding; for CCW winding, outward
                // is on the right side, so flip.
                var tangent = line.EndPoint.AngleFrom(line.StartPoint);
                normal = tangent + Math.Angle.HalfPI;
                if (winding == RotationType.CCW)
                    normal += System.Math.PI;
            }
            else if (entity is Arc arc)
            {
                // Radial direction from center to point.
                // Flip when the arc direction differs from the contour winding —
                // that indicates a concave feature where radial points inward.
                normal = point.AngleFrom(arc.Center);
                if (arc.Rotation != winding)
                    normal += System.Math.PI;
            }
            else if (entity is Circle circle)
            {
                // Radial outward — always correct regardless of winding
                normal = point.AngleFrom(circle.Center);
            }
            else
            {
                normal = 0;
            }

            // For internal contours, flip the normal (point into scrap)
            if (contourType == ContourType.Internal || contourType == ContourType.ArcCircle)
                normal += System.Math.PI;

            return Math.Angle.NormalizeRad(normal);
        }

        public static RotationType DetermineWinding(Shape shape)
        {
            if (shape.Entities.Count == 1 && shape.Entities[0] is Circle circle)
                return circle.Rotation;

            var polygon = shape.ToPolygon();

            if (polygon.Vertices.Count < 3)
                return RotationType.CCW;

            return polygon.RotationDirection();
        }

        private LeadIn ClampLeadInForCircle(
            LeadIn leadIn,
            Circle circle,
            Vector contourPoint,
            double normalAngle
        )
        {
            if (leadIn is NoLeadIn || Parameters.PierceClearance <= 0)
                return leadIn;

            var piercePoint = leadIn.GetPiercePoint(contourPoint, normalAngle);
            var maxRadius = circle.Radius - Parameters.PierceClearance;
            if (maxRadius <= 0)
                return leadIn;

            var distFromCenter = piercePoint.DistanceTo(circle.Center);
            if (distFromCenter <= maxRadius)
                return leadIn;

            // Compute max distance from contourPoint toward piercePoint that stays
            // inside a circle of radius maxRadius centered at circle.Center.
            // Solve: |contourPoint + t*d - center|^2 = maxRadius^2
            var currentDist = contourPoint.DistanceTo(piercePoint);
            if (currentDist < Math.Tolerance.Epsilon)
                return leadIn;

            var dx = (piercePoint.X - contourPoint.X) / currentDist;
            var dy = (piercePoint.Y - contourPoint.Y) / currentDist;
            var vx = contourPoint.X - circle.Center.X;
            var vy = contourPoint.Y - circle.Center.Y;

            var b = 2.0 * (vx * dx + vy * dy);
            var c = vx * vx + vy * vy - maxRadius * maxRadius;
            var discriminant = b * b - 4.0 * c;

            if (discriminant < 0)
                return leadIn;

            var t = (-b + System.Math.Sqrt(discriminant)) / 2.0;
            if (t <= 0)
                return leadIn;

            var scale = t / currentDist;
            if (scale >= 1.0)
                return leadIn;

            return leadIn.Scale(scale);
        }

        private LeadIn SelectLeadIn(ContourType contourType)
        {
            return contourType switch
            {
                ContourType.ArcCircle => Parameters.ArcCircleLeadIn ?? Parameters.InternalLeadIn,
                ContourType.Internal => Parameters.InternalLeadIn,
                _ => Parameters.ExternalLeadIn,
            };
        }

        private LeadOut SelectLeadOut(ContourType contourType)
        {
            return contourType switch
            {
                ContourType.ArcCircle => Parameters.ArcCircleLeadOut ?? Parameters.InternalLeadOut,
                ContourType.Internal => Parameters.InternalLeadOut,
                _ => Parameters.ExternalLeadOut,
            };
        }

        private static Shape TrimShapeForTab(Shape shape, Vector center, double tabSize)
        {
            var tabCircle = new Circle(center, tabSize);
            var entities = new List<Entity>(shape.Entities);

            // Trim end: walk backward removing entities inside the tab circle
            while (entities.Count > 0)
            {
                var entity = entities[entities.Count - 1];
                if (entity.Intersects(tabCircle, out var pts) && pts.Count > 0)
                {
                    // Find intersection furthest from center (furthest along path from end)
                    var best = pts[0];
                    var bestDist = best.DistanceTo(center);
                    for (var j = 1; j < pts.Count; j++)
                    {
                        var dist = pts[j].DistanceTo(center);
                        if (dist > bestDist)
                        {
                            best = pts[j];
                            bestDist = dist;
                        }
                    }

                    if (entity is Line line)
                    {
                        var (first, _) = line.SplitAt(best);
                        entities.RemoveAt(entities.Count - 1);
                        if (first != null)
                            entities.Add(first);
                    }
                    else if (entity is Arc arc)
                    {
                        var (first, _) = arc.SplitAt(best);
                        entities.RemoveAt(entities.Count - 1);
                        if (first != null)
                            entities.Add(first);
                    }

                    break;
                }

                // No intersection — entity is entirely inside circle, remove it
                if (EntityStartPoint(entity).DistanceTo(center) <= tabSize + Tolerance.Epsilon)
                {
                    entities.RemoveAt(entities.Count - 1);
                    continue;
                }

                break;
            }

            var result = new Shape();
            result.Entities.AddRange(entities);
            return result;
        }

        private static Vector EntityStartPoint(Entity entity)
        {
            if (entity is Line line)
                return line.StartPoint;
            if (entity is Arc arc)
                return arc.StartPoint();
            return Vector.Zero;
        }

        private List<ICode> ConvertShapeToMoves(
            Shape shape,
            Vector startPoint,
            LayerType layer = LayerType.Display
        )
        {
            var moves = new List<ICode>();

            foreach (var entity in shape.Entities)
            {
                if (entity is Line line)
                {
                    moves.Add(new LinearMove(line.EndPoint) { Layer = layer });
                }
                else if (entity is Arc arc)
                {
                    moves.Add(
                        new ArcMove(
                            arc.EndPoint(),
                            arc.Center,
                            arc.IsReversed ? RotationType.CW : RotationType.CCW
                        )
                        {
                            Layer = layer,
                        }
                    );
                }
                else if (entity is Circle circle)
                {
                    moves.Add(
                        new ArcMove(startPoint, circle.Center, circle.Rotation) { Layer = layer }
                    );
                }
                else
                {
                    throw new System.InvalidOperationException(
                        $"Unsupported entity type: {entity.Type}"
                    );
                }
            }

            return moves;
        }

        private static Vector GetShapeStartPoint(Shape shape)
        {
            var first = shape.Entities[0];
            if (first is Line line)
                return line.StartPoint;
            if (first is Arc arc)
                return arc.StartPoint();
            if (first is Circle circle)
                return new Vector(circle.Center.X + circle.Radius, circle.Center.Y);
            return Vector.Zero;
        }
    }
}
