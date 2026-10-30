using System;
using System.Collections.Generic;
using System.Linq;
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
            RefuseDisplayPerimeter(entities);
            var breakCuts = TakeBreakCuts(entities);
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
            EmitBreakCuts(result, breakCuts, BreakCutContours(profile));

            foreach (var entry in cutoutEntries)
            {
                if (IsTabbedCutout(entry.Shape))
                    EmitTabbedCutout(result, entry.Shape);
                else if (!entry.Shape.IsClosed())
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

        // These tools would lead the outline or mistake the largest cutout for it (an External lead
        // pierced on the blank). Only the cutting planner knows every contour of such a part is a hole.
        private static void RefuseDisplayPerimeter(List<Entity> entities)
        {
            if (entities.Exists(e => e.Layer == SpecialLayers.DisplayPerimeter))
                throw new InvalidOperationException(DisplayPerimeterTail.LeadInRefusal);
        }

        public CuttingResult ApplySingle(
            Program partProgram,
            Vector point,
            Entity entity,
            ContourType contourType
        )
        {
            var entities = partProgram.ToGeometry();
            RefuseDisplayPerimeter(entities);
            var breakCuts = TakeBreakCuts(entities);
            entities.RemoveAll(e => e.Layer == SpecialLayers.Rapid);

            var scribeEntities = entities.FindAll(e => e.Layer == SpecialLayers.Scribe);
            entities.RemoveAll(e => e.Layer == SpecialLayers.Scribe);

            var profile = new ShapeProfile(entities);

            var result = new Program(Mode.Absolute);

            EmitScribeContours(result, scribeEntities);
            EmitBreakCuts(result, breakCuts, BreakCutContours(profile));

            // Find the target shape that contains the clicked entity
            var (targetShape, matchedEntity) = FindTargetShape(profile, point, entity);

            // Emit cutouts â€” only the target gets lead-in/out (skip open contours)
            foreach (var cutout in profile.Cutouts)
            {
                // A tabbed cutout's pieces are always entered from its slug, like break cuts.
                if (IsTabbedCutout(cutout))
                {
                    EmitTabbedCutout(result, cutout);
                }
                else if (!cutout.IsClosed())
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

        /// <param name="contourParameters">Per-contour settings by contour ordinal (a drawing pin's
        /// own lead-in, lead-out and tab); a null list or entry uses <see cref="Parameters"/>.</param>
        internal Program EmitPrepared(Shape[] shapes, List<Entity> scribes,
            IReadOnlyList<CuttingPlanning.ContourChoice> choices,
            IReadOnlyList<CuttingParameters> contourParameters = null, IReadOnlyList<Shape> breakCuts = null,
            IReadOnlyList<Shape> tabbedCutouts = null, int? perimeterOrdinal = null, Shape displayTail = null)
        {
            var result = new Program(Mode.Absolute);
            EmitScribeContours(result, scribes);
            EmitBreakCuts(result, breakCuts, shapes);
            foreach (var cutout in tabbedCutouts ?? Array.Empty<Shape>())
                EmitTabbedCutout(result, cutout);
            EmitChosenContours(result, shapes, choices, contourParameters, perimeterOrdinal ?? shapes.Length - 1);
            if (displayTail != null)
            {
                // Display only, after every cut: executed reads and posts stop at its marker.
                var start = GetShapeStartPoint(displayTail);
                DisplayPerimeterTail.Append(result, ConvertShapeToMoves(displayTail, start, LayerType.DisplayPerimeter), start);
            }
            result.Mode = Mode.Incremental;
            return result;
        }

        // Diagnostic seam for candidate lead validation (PreparedContours
        // .EmitCandidateForValidation): emit a single already-validated choice with its
        // ORIGINAL contour type â€” the last shape is External even when emitted alone, so a
        // perimeter never degrades to a hole because it was isolated. Output must not be
        // installed on a Part or accepted as a complete plan.
        internal Program EmitCandidateIsolated(Shape[] shapes, List<Entity> scribes,
            CuttingPlanning.ContourChoice choice, IReadOnlyList<CuttingParameters> contourParameters = null,
            int? perimeterOrdinal = null)
        {
            var result = new Program(Mode.Absolute);
            EmitScribeContours(result, scribes);
            EmitChosenContours(result, shapes, new[] { choice }, contourParameters, perimeterOrdinal ?? shapes.Length - 1);
            result.Mode = Mode.Incremental;
            return result;
        }

        // One contour per choice in order; the perimeter is always External. Without a cut perimeter
        // (a display perimeter outside) perimeterOrdinal is past the last shape: every contour is a hole.
        // Contour emission itself reads only the contour's own geometry and settings, never
        // prior choices â€” the differential tests in ContourCandidateEmissionTests pin this.
        private void EmitChosenContours(Program result, Shape[] shapes,
            IReadOnlyList<CuttingPlanning.ContourChoice> choices, IReadOnlyList<CuttingParameters> contourParameters,
            int perimeterOrdinal)
        {
            var shared = Parameters;
            try
            {
                foreach (var choice in choices)
                {
                    // A contour's own settings apply to its emission alone; scribes and every
                    // other contour keep the shared settings.
                    Parameters = contourParameters?[choice.ContourOrdinal] ?? shared;
                    var shape = shapes[choice.ContourOrdinal];
                    EmitContour(result, shape, choice.Point, shape.Entities[choice.EntityOrdinal],
                        choice.ContourOrdinal == perimeterOrdinal ? ContourType.External : null,
                        exactCirclePrograms: true, scrapPierce: choice.Pierce,
                        stopAtCenterLine: contourParameters?[choice.ContourOrdinal] == null);
                }
            }
            finally
            {
                Parameters = shared;
            }
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
            bool exactCirclePrograms = false,
            Vector? scrapPierce = null,
            bool stopAtCenterLine = false
        )
        {
            var contourType = forceType ?? DetectContourType(shape);
            var winding = DetermineWinding(shape);
            var normal = ComputeNormal(point, entity, contourType, winding);

            var leadIn = SelectLeadIn(contourType);
            var leadOut = SelectLeadOut(contourType);
            // A cutout the planner pierced by erosion is too small for its configured leads.
            if (scrapPierce != null && contourType != ContourType.External)
                leadOut = new NoLeadOut();

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

                if (scrapPierce is { } holePierce)
                    leadIn = StraightLeadIn(point, holePierce, out normal);
                else
                {
                    if (stopAtCenterLine)
                        leadIn = StopAtCenterLine(shape, point, leadIn, normal, winding);
                    leadIn = ClampLeadInForCircle(leadIn, circle, point, normal,
                        Parameters.GetEffectivePierceClearance());
                    leadIn = ResolvePierceZone(shape, point, leadIn, normal, Parameters.GetPierceZoneRadius(), winding);
                }

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

            var turn = LeadTurn(contourType, winding);
            double leadInNormal;
            // A planner-placed scrap pierce replaces the configured lead-in with a straight one.
            if (scrapPierce is { } pierce)
                leadIn = StraightLeadIn(point, pierce, out leadInNormal);
            else
            {
                leadIn = ResolveLeadIn(shape, point, entity, contourType, leadIn, winding,
                    Parameters.GetEffectivePierceClearance(), out leadInNormal);
                if (contourType == ContourType.Internal)
                {
                    if (stopAtCenterLine)
                        leadIn = StopAtCenterLine(shape, point, leadIn, leadInNormal, turn);
                    leadIn = ResolvePierceZone(shape, point, leadIn, leadInNormal, Parameters.GetPierceZoneRadius(), turn);
                }
            }
            program.Codes.AddRange(leadIn.Generate(point, leadInNormal, turn));

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
                    Parameters.GetEffectivePierceClearance(), out leadOutNormal);
            }

            program.Codes.AddRange(ConvertShapeToMoves(reindexedShape, point));
            program.Codes.AddRange(leadOut.Generate(leadOutPoint, leadOutNormal, turn));
        }

        /// <summary>
        /// The direction a lead arc turns to meet a contour of <paramref name="winding"/>
        /// tangentially, in the cut direction. Normals point away from the material, so the arc
        /// center lies outside an outside perimeter (turning against its winding) and inside a
        /// cutout (turning with it).
        /// </summary>
        public static RotationType LeadTurn(ContourType contourType, RotationType winding) =>
            contourType == ContourType.External
                ? winding == RotationType.CW ? RotationType.CCW : RotationType.CW
                : winding;

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
        /// the two edges meeting at the corner was picked. A curved lead-in at a convex
        /// perimeter corner uses the normal of the edge cut first, so its arc meets that
        /// edge tangentially. Other styles and contour types keep
        /// <see cref="ComputeLeadInNormal"/>.
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
            if (contourType != ContourType.External || !TryGetCorner(shape, point, entity, out var corner))
                return leadIn;

            if (leadIn is ArcLeadIn or LineArcLeadIn or CleanHoleLeadIn)
            {
                // The arc meets the edge cut first tangentially; its center sits off that edge,
                // which at a convex corner keeps the whole arc clear of the part.
                if (ClassifyCorner(corner, winding) is CornerKind.Convex or CornerKind.Smooth)
                    normal = ComputeNormal(point, corner.Outgoing, contourType, winding);
                return leadIn;
            }

            if (leadIn is not LineLeadIn line)
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
        /// bisects the notch. An <see cref="ArcLeadOut"/> at a convex corner uses the normal of
        /// the edge cut last. Other styles and contour types keep the entity normal.
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
            if (contourType != ContourType.External || !TryGetCorner(shape, point, entity, out var corner))
                return leadOut;

            if (leadOut is ArcLeadOut)
            {
                // The arc leaves the edge cut last tangentially.
                if (ClassifyCorner(corner, winding) is CornerKind.Convex or CornerKind.Smooth)
                    normal = ComputeNormal(point, corner.Incoming, contourType, winding);
                return leadOut;
            }

            if (leadOut is not LineLeadOut line)
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

        internal enum CornerKind
        {
            Convex,
            Reflex,
            Smooth,
            Cusp,
        }

        /// <summary>
        /// A vertex classified for automatic start-point planning: what kind of turn the
        /// contour makes there, and the travel tangents of the two edges it joins. Read-only
        /// so callers cannot mutate the contour; winding is derived the same way
        /// <see cref="EmitContour"/> derives it, so the kind matches actual emission.
        /// </summary>
        internal readonly record struct AutomaticCorner(CornerKind Kind, Vector TangentIn, Vector TangentOut);

        /// <summary>
        /// Classification query shared with entry planning: the turn at <paramref name="point"/>
        /// on a closed line/arc contour, using the same corner geometry and winding derivation
        /// as emission. False when <paramref name="point"/> is not a shared vertex of two
        /// chainable entities (an interior point, an open contour, a degenerate or non-finite
        /// corner). Lead generation itself is not involved.
        /// </summary>
        internal static bool TryClassifyAutomaticStartCorner(
            Shape shape,
            Vector point,
            Entity entity,
            out AutomaticCorner corner)
        {
            if (!TryGetCorner(shape, point, entity, out var raw))
            {
                corner = default;
                return false;
            }

            corner = new AutomaticCorner(
                ClassifyCorner(raw, DetermineWinding(shape)), raw.TangentIn, raw.TangentOut);
            return true;
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
        private static CornerKind ClassifyCorner(ContourCorner corner, RotationType winding) =>
            ClassifyTurn(corner.TangentIn, corner.TangentOut, winding);

        /// <summary>
        /// The corner a contour of <paramref name="winding"/> would make turning from travel
        /// direction <paramref name="tangentIn"/> to <paramref name="tangentOut"/>, classified
        /// exactly as a shared vertex is (see <see cref="TryClassifyAutomaticStartCorner"/>).
        /// </summary>
        internal static CornerKind ClassifyTurn(Vector tangentIn, Vector tangentOut, RotationType winding)
        {
            var cross = tangentIn.X * tangentOut.Y - tangentIn.Y * tangentOut.X;
            var dot = tangentIn.DotProduct(tangentOut);
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
            // Sum unit normals rather than averaging angles (which fails at 0/2Ï€).
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
                // Perpendicular to line direction: tangent + Ï€/2 = left side.
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
                // Flip when the arc direction differs from the contour winding â€”
                // that indicates a concave feature where radial points inward.
                normal = point.AngleFrom(arc.Center);
                if (arc.Rotation != winding)
                    normal += System.Math.PI;
            }
            else if (entity is Circle circle)
            {
                // Radial outward â€” always correct regardless of winding
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

        /// <summary>
        /// Shortens a round hole's lead-in so its pierce keeps <paramref name="clearance"/> from
        /// the hole wall. A hole no wider than twice the clearance keeps the lead as it is.
        /// Shared by program generation and the manual placement preview.
        /// </summary>
        public static LeadIn ClampLeadInForCircle(
            LeadIn leadIn,
            Circle circle,
            Vector contourPoint,
            double normalAngle,
            double clearance
        )
        {
            if (leadIn is NoLeadIn || clearance <= 0)
                return leadIn;

            var piercePoint = leadIn.GetPiercePoint(contourPoint, normalAngle, circle.Rotation);
            var maxRadius = circle.Radius - clearance;
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

        /// <summary>
        /// Keeps a hole's lead-in pierce <paramref name="pierceRadius"/> clear of the hole wall.
        /// A lead-in whose pierce already keeps it, or that pierces on the contour itself, is
        /// returned as given. Otherwise the pierce moves straight in from the wall to the hole's
        /// center line, or the lead's own length in when that comes first (see
        /// <see cref="StopAtCenterLine"/>). A lead that cannot keep the zone there moves onto the
        /// line from <paramref name="point"/> toward the nearest point that keeps it (see
        /// <see cref="ErodedPierce"/>) or else the hole's pole of inaccessibility (the center of a
        /// round hole), as close to the contour as the zone allows. Either way the lead becomes
        /// a straight line from there, generated with <paramref name="normal"/>. A hole too small
        /// for the zone is pierced at the pole itself, its best clearance; the lead validator and
        /// pre-post review still report the shortfall. Reads only the hole's own geometry, so it
        /// knows nothing of parts nested inside the hole. Shared by program generation and the
        /// manual placement preview. <paramref name="turn"/> is the hole's
        /// <see cref="LeadTurn"/>, which places a curved lead's pierce.
        /// </summary>
        public static LeadIn ResolvePierceZone(Shape hole, Vector point, LeadIn leadIn, double normal,
            double pierceRadius, RotationType turn)
        {
            if (pierceRadius <= 0 || !double.IsFinite(pierceRadius) || leadIn is NoLeadIn
                || !hole.IsClosed())
                return leadIn;

            var circle = hole.Entities.Count == 1 ? hole.Entities[0] as Circle : null;
            var polygon = circle == null
                ? hole.ToPolygonWithTolerance(System.Math.Max(pierceRadius / 100, Math.Tolerance.ChainTolerance))
                : null;
            var configured = leadIn.GetPiercePoint(point, normal, turn);
            if (Clear(configured))
                return leadIn;

            var depth = CenterLineDepth(hole, point, normal, configured.DistanceTo(point));
            if (depth > Math.Tolerance.Epsilon && Clear(point + Inward(normal) * depth))
                return new LineLeadIn { Length = depth, ApproachAngle = 90 };

            var pole = circle?.Center ?? PolyLabel.Find(polygon, null,
                System.Math.Max(pierceRadius / 100, Math.Tolerance.ChainTolerance), PoleSearchCells);
            if (!Inside(pole))
                return leadIn;

            // The pole of a letter can be a stroke away: head for the stroke beside the entry, on
            // its center line when the lead reaches that far.
            var reach = configured.DistanceTo(point);
            var toward = pole;
            if (circle == null && ErodedPierce.Find(hole, System.Math.Max(pierceRadius, reach), new[] { point }, 1)
                .Pierces is [var eroded] && Clear(eroded))
                toward = eroded;

            // Clearance grows monotonically along this line in a convex hole, so the bisection
            // finds the shortest clear lead; in other holes it still ends on a clear point.
            var pierce = toward;
            if (Clear(toward) && (reach < pierceRadius || toward == pole))
            {
                double low = 0, high = 1;
                for (var i = 0; i < 40; i++)
                {
                    var middle = (low + high) / 2;
                    if (Clear(point + (toward - point) * middle))
                        high = middle;
                    else
                        low = middle;
                }
                pierce = point + (toward - point) * high;
            }

            var length = pierce.DistanceTo(point);
            if (length <= Math.Tolerance.Epsilon)
                return leadIn;
            var direction = System.Math.Atan2(pierce.Y - point.Y, pierce.X - point.X);
            return new LineLeadIn
            {
                Length = length,
                ApproachAngle = Angle.ToDegrees(Angle.NormalizeRad(direction - normal + Angle.HalfPI)),
            };

            bool Inside(Vector p) => circle != null
                ? p.DistanceTo(circle.Center) < circle.Radius
                : polygon.ContainsPoint(p);

            bool Clear(Vector p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && Inside(p)
                && hole.ClosestPointTo(p, out _).DistanceTo(p) >= pierceRadius;
        }

        /// <summary>
        /// Keeps a cutout's lead-in out of the cutout's far half. A lead longer than the cutout is
        /// deep at its entry, measured straight in from the wall to the center line where the walls
        /// meet as the cutout erodes, reaches past the center line or runs along a narrow stroke, as
        /// does one whose own line gets closer to a wall again. It becomes a straight one, generated
        /// with <paramref name="normal"/>, from that center line (the center of a round hole) or
        /// from the lead's own length in when that comes first. Any other lead keeps its shape, and
        /// one that leaves a cutout other than a round hole is left for the lead check to refuse.
        /// </summary>
        public static LeadIn StopAtCenterLine(Shape hole, Vector point, LeadIn leadIn, double normal, RotationType turn)
        {
            if (leadIn is NoLeadIn || !hole.IsClosed())
                return leadIn;
            var pierce = leadIn.GetPiercePoint(point, normal, turn);
            var length = pierce.DistanceTo(point);
            if (!double.IsFinite(length) || length <= Math.Tolerance.Epsilon)
                return leadIn;

            var circle = hole.Entities.Count == 1 ? hole.Entities[0] as Circle : null;
            var along = pierce - point;
            var inward = Inward(normal);
            // A pierce as far from every wall as it is deep has not come near another wall.
            var inside = circle == null || pierce.DistanceTo(circle.Center) < circle.Radius;
            if (inside && Clearance(hole, pierce) >= along.X * inward.X + along.Y * inward.Y - Math.Tolerance.Epsilon)
                return leadIn;

            var depth = CenterLineDepth(hole, point, normal, length);
            if (depth <= Math.Tolerance.Epsilon
                || inside && depth >= length - Math.Tolerance.Epsilon && !PeaksBeforeItsEnd(hole, point, along))
                return leadIn;
            if (circle == null)
            {
                var polygon = hole.ToPolygonWithTolerance(Math.Tolerance.ChainTolerance);
                for (var i = 1; i <= CenterLineSteps; i++)
                    if (!polygon.ContainsPoint(point + along * ((double)i / CenterLineSteps)))
                        return leadIn;
            }
            return new LineLeadIn { Length = depth, ApproachAngle = 90 };
        }

        // How far a straight line in from the wall runs, up to maxDepth, before it reaches the
        // center line: the distance to the wall grows at a steady rate until a second wall is as
        // near, then levels off along a stroke or falls.
        private static double CenterLineDepth(Shape hole, Vector point, double normal, double maxDepth)
        {
            if (hole.Entities.Count == 1 && hole.Entities[0] is Circle circle)
                return System.Math.Min(maxDepth, circle.Radius);

            var inward = Inward(normal);
            var step = maxDepth / CenterLineSteps;
            var rate = Clearance(hole, point + inward * step) / step;
            // A corner's curved lead keeps an edge's normal, which runs along the other edge.
            if (rate < MinimumCenterLineRate)
                return maxDepth;
            bool Rising(double depth) => Clearance(hole, point + inward * (depth + step / CenterLineSteps))
                - Clearance(hole, point + inward * depth) >= rate / 2 * (step / CenterLineSteps);
            for (var i = 1; i < CenterLineSteps; i++)
            {
                if (Rising(step * i))
                    continue;
                double low = step * (i - 1), high = step * i;
                for (var refine = 0; refine < CenterLineRefinements; refine++)
                {
                    var middle = (low + high) / 2;
                    if (Rising(middle))
                        low = middle;
                    else
                        high = middle;
                }
                return low;
            }
            return maxDepth;
        }

        private static bool PeaksBeforeItsEnd(Shape hole, Vector point, Vector along)
        {
            var peak = 0.0;
            for (var i = 1; i <= CenterLineSteps; i++)
            {
                var clearance = Clearance(hole, point + along * ((double)i / CenterLineSteps));
                if (clearance < peak - Math.Tolerance.Epsilon)
                    return true;
                peak = System.Math.Max(peak, clearance);
            }
            return false;
        }

        private static double Clearance(Shape hole, Vector point) => hole.ClosestPointTo(point, out _).DistanceTo(point);

        private static Vector Inward(double normal) => new(System.Math.Cos(normal), System.Math.Sin(normal));

        /// <summary>
        /// A straight lead-in from <paramref name="pierce"/> to <paramref name="point"/>, with the
        /// normal to generate it with.
        /// </summary>
        private static LeadIn StraightLeadIn(Vector point, Vector pierce, out double normal)
        {
            normal = System.Math.Atan2(pierce.Y - point.Y, pierce.X - point.X);
            return new LineLeadIn { Length = pierce.DistanceTo(point), ApproachAngle = 90 };
        }

        // Bounds the pole search on long, thin holes; the result is checked before use.
        private const int PoleSearchCells = 4096;

        // Samples along a lead when looking for the center line, then the steps narrowing in on it.
        private const int CenterLineSteps = 64;
        private const int CenterLineRefinements = 16;
        private const double MinimumCenterLineRate = 0.01;

        private LeadIn SelectLeadIn(ContourType contourType)
        {
            return contourType switch
            {
                ContourType.Internal or ContourType.ArcCircle => Parameters.InternalLeadIn,
                _ => Parameters.ExternalLeadIn,
            };
        }

        private LeadOut SelectLeadOut(ContourType contourType)
        {
            return contourType switch
            {
                ContourType.Internal or ContourType.ArcCircle => Parameters.InternalLeadOut,
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

                // No intersection â€” entity is entirely inside circle, remove it
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
            var position = startPoint;

            foreach (var entity in shape.Entities)
            {
                if (entity is Line line)
                {
                    moves.Add(new LinearMove(line.EndPoint) { Layer = layer });
                    position = line.EndPoint;
                }
                else if (entity is Arc arc)
                {
                    // A contour entered away from its seam crosses the seam's admitted closure gap
                    // mid-cut. Starting the arc there would move its start off its own circle, so
                    // bridge the gap with a straight move and cut the arc exactly as drawn. A lead-in
                    // must join the contour itself, so an entry on the seam is left as it was.
                    var arcStart = arc.StartPoint();
                    var gap = position.DistanceTo(arcStart);
                    if (moves.Count > 0 && gap > Diagnostics.PostVerificationGeometry.Epsilon
                        && CuttingPlanning.NominalContourClosure.IsClosed(position, arcStart))
                        moves.Add(new LinearMove(arcStart) { Layer = layer });
                    position = arc.EndPoint();
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
                    position = startPoint;
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

        /// <summary>
        /// Removes break-cut entities from <paramref name="entities"/> (in program order) and
        /// returns them as travel-ordered open chains, one per uninterrupted run.
        /// </summary>
        internal static List<Shape> TakeBreakCuts(List<Entity> entities)
        {
            var cuts = new List<Shape>();
            Shape current = null;
            foreach (var entity in entities)
            {
                if (entity.Layer != SpecialLayers.Break)
                {
                    current = null;
                    continue;
                }
                if (current == null
                    || EntityEndPoint(current.Entities[^1]).DistanceTo(EntityStartPoint(entity)) > Tolerance.Epsilon)
                {
                    current = new Shape();
                    cuts.Add(current);
                }
                current.Entities.Add(entity);
            }
            entities.RemoveAll(e => e.Layer == SpecialLayers.Break);
            foreach (var cut in cuts)
                cut.UpdateBounds();
            return cuts;
        }

        // A clean program's break cut travels with its scrap on the left. It is entered from the
        // scrap, with the internal lead-in (holes are pierced in scrap the same way), at whichever
        // end has more scrap before the part's contours: near the outline the scrap narrows to a
        // wedge. NoLeadIn pierces on the line. There is no lead-out: the cut ends at a tab, which a
        // run-out would cut into. Entering at the far end cuts it the other way round.
        private void EmitBreakCuts(Program program, IReadOnlyList<Shape> cuts, IReadOnlyList<Shape> contours)
        {
            if (cuts == null)
                return;
            var leadIn = Parameters?.InternalLeadIn ?? new NoLeadIn();
            foreach (var cut in cuts)
            {
                var (entry, scrap, fromEnd) = BreakCutEntry(cut, contours);
                var path = cut;
                if (fromEnd)
                {
                    path = (Shape)cut.Clone();
                    path.Reverse();
                }
                var normal = Angle.NormalizeRad(System.Math.Atan2(scrap.Y, scrap.X));
                program.Codes.AddRange(leadIn.Generate(entry, normal, fromEnd ? RotationType.CW : RotationType.CCW));
                program.Codes.AddRange(ConvertShapeToMoves(path, entry, LayerType.Break));
            }
        }

        /// <summary>
        /// Where a break cut (traveling with its scrap on the left) is entered: the end with more
        /// scrap before <paramref name="contours"/>, the unit direction into the scrap there, and
        /// whether that is the cut's far end.
        /// </summary>
        public static (Vector Entry, Vector ScrapDirection, bool FromEnd) BreakCutEntry(Shape cut,
            IReadOnlyList<Shape> contours)
        {
            var start = EntityStartPoint(cut.Entities[0]);
            var end = EntityEndPoint(cut.Entities[^1]);
            var startScrap = LeftOf(Tangent(cut.Entities[0], atEnd: false));
            var endScrap = LeftOf(Tangent(cut.Entities[^1], atEnd: true));
            return Room(end, endScrap, contours) > Room(start, startScrap, contours)
                ? (end, endScrap, true)
                : (start, startScrap, false);
        }

        /// <summary>Whether <paramref name="shape"/> has <see cref="SpecialLayers.Tab"/> spans: a tabbed cutout.</summary>
        internal static bool IsTabbedCutout(Shape shape) => shape.Entities.Exists(e => e.Layer == SpecialLayers.Tab);

        /// <summary>
        /// The cut pieces of a closed tabbed cutout in its own travel order, starting after a tab:
        /// each maximal run of entities that are not on the tab layer.
        /// </summary>
        internal static List<Shape> TabbedCutoutPieces(Shape cutout)
        {
            var entities = cutout.Entities;
            var count = entities.Count;
            var first = -1;
            for (var i = 0; i < count && first < 0; i++)
                if (entities[i].Layer != SpecialLayers.Tab && entities[(i + count - 1) % count].Layer == SpecialLayers.Tab)
                    first = i;
            var pieces = new List<Shape>();
            if (first < 0)
                return pieces;
            Shape current = null;
            for (var k = 0; k < count; k++)
            {
                var entity = entities[(first + k) % count];
                if (entity.Layer == SpecialLayers.Tab)
                {
                    current = null;
                    continue;
                }
                if (current == null)
                {
                    current = new Shape();
                    pieces.Add(current);
                }
                current.Entities.Add(entity);
            }
            foreach (var piece in pieces)
                piece.UpdateBounds();
            return pieces;
        }

        // A tabbed cutout is cut as open pieces between its tabs, in the cutout's own direction.
        // Each piece is entered from the slug with the internal lead-in (NoLeadIn pierces on the
        // contour) and has no lead-out, since it ends at a tab a run-out would cut into. A lead-in
        // that would reach back over the tab behind its piece is mirrored about the entry normal,
        // so it approaches from the piece's side instead. On an arc, a lead ending in an arc would
        // meet the cut tangentially (a contact verification cannot resolve), so it pierces straight
        // in from the same distance.
        private void EmitTabbedCutout(Program program, Shape cutout)
        {
            var winding = DetermineWinding(cutout);
            var leadIn = Parameters?.InternalLeadIn ?? new NoLeadIn();
            foreach (var piece in TabbedCutoutPieces(cutout))
            {
                var first = piece.Entities[0];
                var entry = EntityStartPoint(first);
                var normal = ComputeNormal(entry, first, ContourType.Internal, winding);
                var tangent = TravelTangent(first, entry);
                program.Codes.AddRange(TabbedLeadIn(leadIn, entry, normal, tangent, winding, first is Arc));
                program.Codes.AddRange(ConvertShapeToMoves(piece, entry));
            }
        }

        private static List<ICode> TabbedLeadIn(LeadIn leadIn, Vector entry, double normal, Vector tangent,
            RotationType winding, bool onArc)
        {
            var codes = leadIn.Generate(entry, normal, winding);
            if (!(onArc && codes.Count > 0 && codes[^1] is ArcMove))
            {
                if (ReachBehind(codes, entry, tangent) <= Tolerance.Epsilon)
                    return codes;
                var mirrored = codes.Select(code => Mirror(code, entry, tangent)).ToList();
                if (ReachBehind(mirrored, entry, tangent) <= Tolerance.Epsilon)
                    return mirrored;
            }
            // Reaching both ways, or tangent to an arc: pierce straight in from the same distance.
            var pierce = leadIn.GetPiercePoint(entry, normal, winding);
            return new LineLeadIn { Length = pierce.DistanceTo(entry), ApproachAngle = 90 }.Generate(entry, normal, winding);
        }

        // How far the lead motions of `codes` (absolute, after their first rapid) reach behind
        // `entry`, against the unit travel `tangent`.
        private static double ReachBehind(List<ICode> codes, Vector entry, Vector tangent)
        {
            var behind = new Vector(-tangent.X, -tangent.Y);
            double Along(Vector p) => (p.X - entry.X) * behind.X + (p.Y - entry.Y) * behind.Y;
            var reach = double.NegativeInfinity;
            Vector? position = null;
            foreach (var code in codes)
            {
                if (code is not Motion motion)
                    continue;
                var end = motion.EndPoint;
                if (position is { } start && code is not RapidMove)
                {
                    reach = System.Math.Max(reach, System.Math.Max(Along(start), Along(end)));
                    if (code is ArcMove arc)
                    {
                        var center = arc.CenterPoint;
                        var radius = center.DistanceTo(end);
                        var extreme = new Vector(center.X + behind.X * radius, center.Y + behind.Y * radius);
                        if (OnArc(center, start, end, extreme, arc.Rotation))
                            reach = System.Math.Max(reach, Along(extreme));
                    }
                }
                position = end;
            }
            return reach;
        }

        private static bool OnArc(Vector center, Vector start, Vector end, Vector point, RotationType rotation)
        {
            var a0 = System.Math.Atan2(start.Y - center.Y, start.X - center.X);
            var a1 = System.Math.Atan2(end.Y - center.Y, end.X - center.X);
            var ap = System.Math.Atan2(point.Y - center.Y, point.X - center.X);
            var sign = rotation == RotationType.CCW ? 1 : -1;
            var sweep = Angle.NormalizeRad(sign * (a1 - a0));
            if (sweep <= Tolerance.Epsilon)
                sweep = Angle.TwoPI;
            return Angle.NormalizeRad(sign * (ap - a0)) <= sweep;
        }

        // `code` reflected across the line through `entry` along its normal (negating the
        // tangent component), with arc directions swapped.
        private static ICode Mirror(ICode code, Vector entry, Vector tangent)
        {
            Vector Reflect(Vector p)
            {
                var along = (p.X - entry.X) * tangent.X + (p.Y - entry.Y) * tangent.Y;
                return new Vector(p.X - 2 * along * tangent.X, p.Y - 2 * along * tangent.Y);
            }
            return code switch
            {
                RapidMove rapid => new RapidMove(Reflect(rapid.EndPoint)),
                LinearMove line => new LinearMove(Reflect(line.EndPoint)) { Layer = line.Layer },
                ArcMove arc => new ArcMove(Reflect(arc.EndPoint), Reflect(arc.CenterPoint),
                    arc.Rotation == RotationType.CW ? RotationType.CCW : RotationType.CW)
                { Layer = arc.Layer },
                _ => code,
            };
        }

        private static List<Shape> BreakCutContours(ShapeProfile profile) =>
            new List<Shape>(profile.Cutouts) { profile.Perimeter };

        // Distance from a break cut end along its scrap direction to the nearest contour.
        private static double Room(Vector point, Vector direction, IReadOnlyList<Shape> contours)
        {
            const double reach = 1e6;
            var ray = new Line(point, point + direction * reach);
            var nearest = reach;
            foreach (var contour in contours ?? Array.Empty<Shape>())
                foreach (var entity in contour.Entities)
                    if (entity.Intersects(ray, out var hits))
                        foreach (var hit in hits)
                            nearest = System.Math.Min(nearest, hit.DistanceTo(point));
            return nearest;
        }

        private static Vector LeftOf(Vector tangent)
        {
            var length = System.Math.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y);
            return length <= 0 ? new Vector(0, 1) : new Vector(-tangent.Y / length, tangent.X / length);
        }

        private static Vector Tangent(Entity entity, bool atEnd)
        {
            if (entity is Arc arc)
            {
                var angle = atEnd ? arc.EndAngle : arc.StartAngle;
                return arc.IsReversed
                    ? new Vector(System.Math.Sin(angle), -System.Math.Cos(angle))
                    : new Vector(-System.Math.Sin(angle), System.Math.Cos(angle));
            }
            var line = (Line)entity;
            return line.EndPoint - line.StartPoint;
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
