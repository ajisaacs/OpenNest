using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clipper2Lib;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Math;


namespace OpenNest.Geometry
{
    /// <summary>
    /// Seed-aligns a revised drawing's geometry to an old drawing's drawing-local
    /// frame with a bounded, robust, multi-start rigid ICP in 2D. Intended as an
    /// initial alignment for operator review, never as a proven-unique registration:
    /// a small perimeter edit, a symmetric outline, or a dominant repeated feature
    /// can defeat any local optimizer, and every reason this class reports routes
    /// the pair to the manual overlay. The result never applies reflection.
    /// <para>
    /// Convention: the returned transform maps NEW points into the OLD local frame
    /// — reflect about the X axis (only if the caller explicitly selects a mirror;
    /// this aligner never does), then rotate by <see cref="AlignmentResult.Rotation"/>
    /// radians, then translate. No scale, no shear, ever.
    /// </para>
    /// <para>
    /// Pipeline: material-filter the CNC programs through
    /// <see cref="ConvertProgram.ToGeometry(Program)"/> (rapids and scribe moves are
    /// dropped by layer; nominal cut contours are kept), chain them into contours,
    /// classify outer versus hole roles, flatten with the shared chord-error
    /// machinery, resample each ring at near-uniform arclength with
    /// <see cref="ContourSampler.RingMoves"/>, seed translation from outer geometry
    /// and rotation from minimum-bounding-rectangle angle deltas plus the
    /// 90-degree family and identity, then run a trimmed robust ICP per start that
    /// matches samples to closest bounded target SEGMENTS and solves a proper
    /// 2D rotation+translation least-squares fit (reflection excluded inside the
    /// solve). The outer boundary is fitted first; hole evidence is folded in with
    /// reduced weight so many small holes cannot dominate and a moved hole is
    /// trimmed instead of dragging an unchanged perimeter.
    /// </para>
    /// </summary>
    public static class DrawingAligner
    {
        /// <summary>
        /// Aligns <paramref name="newProgram"/> onto <paramref name="oldProgram"/>.
        /// Both programs are treated as read-only. Throws
        /// <see cref="OperationCanceledException"/> only when cancelled before any
        /// measurable work; work limits reached mid-run are reported as reasons on
        /// the result instead.
        /// </summary>
        public static AlignmentResult Align(
            Program oldProgram,
            Program newProgram,
            AlignmentOptions options = null,
            CancellationToken cancellationToken = default
        )
        {
            options ??= new AlignmentOptions();

            var prepared = Prepare(oldProgram, "old", options, out var oldReason, out var oldError);
            if (prepared == null)
                return Failure(oldReason, oldError);

            var newSide = Prepare(newProgram, "new", options, out var newReason, out var newError);
            if (newSide == null)
                return Failure(newReason, newError);

            var minSamples = System.Math.Max(3, options.MinSamples);
            if (
                newSide.TotalSamples < minSamples
                || prepared.TotalSamples < minSamples
            )
                return Failure(
                    AlignmentReasons.InsufficientSupport,
                    "Too few contour samples to align; increase sampling budget or align manually."
                );

            // Seeds: translation from outer centroid, rotation from MBR angle deltas
            // with the 90-degree family, plus the identity/original-frame candidate.
            // These are local-search starts, not an exhaustive proof of the best pose.
            var seeds = BuildSeeds(prepared, newSide);

            // Stage 1: fit the stable outer boundary only, so many small holes or a
            // moved hole cannot dominate or drag the perimeter.
            var candidates = SearchStarts(prepared, newSide, seeds, options, false, cancellationToken);
            var reflectedCandidates = SearchStarts(prepared, newSide, seeds, options, true, cancellationToken);

            if (candidates.Count == 0)
                return Failure(
                    AlignmentReasons.FailedConvergence,
                    "No candidate pose converged; align manually."
                );

            var result = BuildResult(prepared, newSide, candidates, reflectedCandidates, options);
            return result;
        }

        /// <summary>
        /// Bounded diagnostic intersection-over-union of two drawings' material
        /// regions under the pose (NEW -> OLD convention), for review display only.
        /// Null when regions are unavailable or degenerate. Not a calibrated
        /// probability and never a gate input.
        /// </summary>
        public static double? DiagnosticIoU(
            Program oldProgram,
            Program newProgram,
            double rotation,
            Vector translation,
            bool reflect = false,
            AlignmentOptions options = null
        )
        {
            options ??= new AlignmentOptions();
            var oldSide = Prepare(oldProgram, "old", options, out _, out _);
            var newSide = Prepare(newProgram, "new", options, out _, out _);
            if (oldSide == null || newSide == null)
                return null;

            var candidate = new Candidate
            {
                Rotation = rotation,
                Translation = translation,
            };

            if (!reflect)
                return RegionIoU(oldSide, newSide, candidate, options);

            // Reflect the NEW side about its X axis once for the diagnostic.
            var reflected = new Side
            {
                Outer = Mirror(newSide.Outer),
                Holes = newSide.Holes.Select(Mirror).ToList(),
                TotalSamples = newSide.TotalSamples,
            };
            return RegionIoU(oldSide, reflected, candidate, options);

            static Ring Mirror(Ring ring)
            {
                var pts = ring.Points
                    .Select(p => new Vector(p.X, -p.Y))
                    .ToArray();
                var segments = new Line[pts.Length];
                var kept = 0;
                for (var i = 0; i < pts.Length; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Length];
                    if (a.DistanceTo(b) <= Tolerance.Epsilon)
                        continue;
                    segments[kept++] = new Line(a, b);
                }
                return new Ring
                {
                    Points = pts,
                    Samples = ring.Samples
                        .Select(s => new ContourSample(
                            new Vector(s.Position.X, -s.Position.Y),
                            new Vector(s.Direction.X, -s.Direction.Y),
                            -s.Tangent,
                            s.At
                        ))
                        .ToArray(),
                    Segments = segments.Take(kept).ToArray(),
                    Perimeter = ring.Perimeter,
                };
            }
        }

        // ---------- contour preparation ----------

        private sealed class Ring
        {
            public Vector[] Points; // closed implicitly; no duplicate closing vertex
            public ContourSample[] Samples;
            public Line[] Segments; // target segments, same length as Points (closes implicitly)
            public double Perimeter;
        }

        private sealed class Side
        {
            public Ring Outer;
            public List<Ring> Holes = new();
            public int TotalSamples;

            /// <summary>
            /// Distance at which a correspondence counts as an unmatched (changed)
            /// boundary span instead of fit error, for COVERAGE reporting only:
            /// relative to the part size, so a moved hole on a small part cannot
            /// hide inside a generous absolute fit-trim envelope.
            /// </summary>
            public double SpanDistance = double.PositiveInfinity;
        }

        private static Side Prepare(
            Program pgm,
            string which,
            AlignmentOptions options,
            out AlignmentReasons reason,
            out string error
        )
        {
            reason = AlignmentReasons.InvalidGeometry;
            error = null;
            var side = new Side();

            if (pgm == null)
            {
                error = $"The {which} program is missing.";
                return null;
            }

            var entities = ConvertProgram
                .ToGeometry(pgm)
                .Where(e => SpecialLayers.IsMaterial(e.Layer))
                .ToList();

            if (entities.Count == 0)
            {
                error = $"The {which} drawing has no material geometry.";
                return null;
            }

            List<Shape> shapes;
            try
            {
                shapes = ShapeBuilder.GetShapes(entities, Tolerance.ChainTolerance);
            }
            catch (Exception ex)
            {
                error = $"The {which} drawing geometry cannot be chained into contours: {ex.Message}";
                return null;
            }

            if (shapes.Count == 0)
            {
                error = $"The {which} drawing has no contours.";
                return null;
            }

            var contours = ContourInfo.Classify(shapes);

            var outerInfo = contours.FirstOrDefault(c => c.Type == ContourClassification.Perimeter);
            if (outerInfo == null)
            {
                error = $"The {which} drawing has no closed outer contour.";
                return null;
            }
            // Flatten closes its output; an open outer contour must not be silently
            // closed into a measurable ring.
            if (!outerInfo.Shape.IsClosed())
            {
                error = $"The outer contour of the {which} drawing is not closed.";
                return null;
            }

            // Validate closure and finiteness before flattening; Flatten closes its
            // output and is not an input validator.
            foreach (var info in contours.Where(c => c.Type != ContourClassification.Open))
            {
                if (!info.Shape.IsClosed())
                {
                    error = $"A closed contour in the {which} drawing is not closed.";
                    return null;
                }
                foreach (var entity in info.Shape.Entities)
                {
                    foreach (var pt in EntityPoints(entity))
                    {
                        if (double.IsNaN(pt.X) || double.IsNaN(pt.Y) || double.IsInfinity(pt.X) || double.IsInfinity(pt.Y))
                        {
                            error = $"The {which} drawing contains nonfinite coordinates.";
                            return null;
                        }
                    }
                }
            }

            // Reject unsupported topology: every non-outer closed contour must lie
            // inside the outer contour, otherwise this is disconnected material, not
            // a part with holes.
            var outerFlat = ClipperBridge.Flatten(outerInfo.Shape, options.FlattenTolerance, false);
            var outerRing = ToRing(outerFlat.Vertices, options, out var outerSpacingError);
            if (outerRing == null)
            {
                error = outerSpacingError != null
                    ? outerSpacingError
                    : $"The outer contour of the {which} drawing has no usable perimeter.";
                return null;
            }
            side.Outer = outerRing;
            side.TotalSamples += outerRing.Samples.Length;

            foreach (var info in contours)
            {
                if (info == outerInfo || info.Type != ContourClassification.Hole)
                    continue;

                var flat = ClipperBridge.Flatten(info.Shape, options.FlattenTolerance, false);
                var ring = ToRing(flat.Vertices, options, out var holeError);
                if (ring == null)
                {
                    error = holeError ?? $"A hole in the {which} drawing has no usable perimeter.";
                    return null;
                }

                var probe = ring.Points[0];
                if (!outerFlat.ContainsPoint(probe) && !OuterContainsVertex(outerFlat, probe, options))
                {
                    error = $"A contour of the {which} drawing lies outside the outer contour; disconnected material is not supported.";
                    return null;
                }

                side.Holes.Add(ring);
                side.TotalSamples += ring.Samples.Length;
            }

            if (side.TotalSamples > options.MaxTotalSamples)
            {
                error = $"The {which} drawing needs more samples ({side.TotalSamples}) than the configured total ({options.MaxTotalSamples}); raise the sampling budget or align manually.";
                reason = AlignmentReasons.InsufficientSupport;
                return null;
            }

            if (side.TotalSamples < System.Math.Max(3, options.MinSamples))
            {
                error = $"The {which} drawing has too few samples to align.";
                reason = AlignmentReasons.InsufficientSupport;
                return null;
            }

            var obb = outerFlat.BoundingBox;
            var diagonal = System.Math.Sqrt(
                (obb.Right - obb.Left) * (obb.Right - obb.Left)
                    + (obb.Top - obb.Bottom) * (obb.Top - obb.Bottom)
            );
            side.SpanDistance = System.Math.Max(8 * options.FlattenTolerance, 0.10 * diagonal);

            return side;
        }

        /// <summary>
        /// Fallback containment when a hole touches the outer contour within
        /// rounding: accept a vertex inside the bounding box expanded by the
        /// flatten tolerance plus one part in a thousand of the part size.
        /// </summary>
        private static bool OuterContainsVertex(Polygon outer, Vector probe, AlignmentOptions options)
        {
            var bb = outer.BoundingBox;
            var margin = options.FlattenTolerance + 0.001 * System.Math.Max(bb.Right - bb.Left, bb.Top - bb.Bottom);
            return probe.X >= bb.Left - margin && probe.X <= bb.Right + margin && probe.Y >= bb.Bottom - margin && probe.Y <= bb.Top + margin;
        }

        private static Ring ToRing(List<Vector> flattened, AlignmentOptions options, out string error)
        {
            error = null;
            var pts = DistinctClosed(flattened);
            if (pts.Count < 3)
                return null;

            double perimeter = 0;
            for (var i = 0; i < pts.Count; i++)
                perimeter += pts[i].DistanceTo(pts[(i + 1) % pts.Count]);
            if (!(perimeter > Tolerance.Epsilon))
                return null;

            // Bound the work: raise the spacing until the ring fits the sample cap.
            var spacing = System.Math.Max(
                options.SamplingSpacing,
                perimeter / System.Math.Max(8, options.MaxSamplesPerRing)
            );

            var samples = new List<ContourSample>();
            try
            {
                ContourSampler.RingMoves(pts, spacing, samples);
            }
            catch (ArgumentException)
            {
                error = "A contour ring is invalid.";
                return null;
            }
            if (samples.Count < 3)
                return null;

            var segments = new Line[pts.Count];
            var kept = 0;
            for (var i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                if (a.DistanceTo(b) <= Tolerance.Epsilon)
                    continue; // zero-length segments are not constraints
                segments[kept++] = new Line(a, b);
            }

            return new Ring
            {
                Points = pts.ToArray(),
                Samples = samples.ToArray(),
                Segments = segments.Take(kept).ToArray(),
                Perimeter = perimeter,
            };
        }

        private static List<Vector> DistinctClosed(List<Vector> vertices)
        {
            var result = new List<Vector>(vertices.Count);
            foreach (var v in vertices)
            {
                if (result.Count > 0)
                {
                    var last = result[^1];
                    if (System.Math.Abs(last.X - v.X) <= Tolerance.Epsilon && System.Math.Abs(last.Y - v.Y) <= Tolerance.Epsilon)
                        continue;
                }
                result.Add(v);
            }
            if (result.Count > 1)
            {
                var first = result[0];
                var last = result[^1];
                if (System.Math.Abs(first.X - last.X) <= Tolerance.Epsilon && System.Math.Abs(first.Y - last.Y) <= Tolerance.Epsilon)
                    result.RemoveAt(result.Count - 1);
            }
            return result;
        }

        private static IEnumerable<Vector> EntityPoints(Entity entity)
        {
            switch (entity)
            {
                case Line line:
                    yield return line.StartPoint;
                    yield return line.EndPoint;
                    break;
                case Arc arc:
                    yield return arc.Center;
                    yield return arc.StartPoint();
                    yield return arc.EndPoint();
                    break;
                case Circle circle:
                    yield return circle.Center;
                    break;
                case Shape shape:
                    foreach (var e in shape.Entities)
                        foreach (var p in EntityPoints(e))
                            yield return p;
                    break;
            }
        }

        // ---------- multi-start search ----------

        private sealed class Candidate
        {
            public double Rotation;
            public Vector Translation;

            /// <summary>Trimmed RMS of the outer-boundary fit (stage 1).</summary>
            public double OuterRms;

            /// <summary>Trimmed RMS including hole evidence; NaN in stage 1.</summary>
            public double CombinedRms = double.NaN;
            public int Iterations;
            public bool Converged;
        }

        private static List<(double Rotation, Vector Translation)> BuildSeeds(Side old, Side @new)
        {
            var seeds = new List<(double, Vector)>();

            void Add(double rotation)
            {
                foreach (var s in seeds)
                    if (System.Math.Abs(NormalizeAngle(s.Item1 - rotation)) < 1e-9)
                        return;
                seeds.Add((rotation, new Vector()));
            }

            Add(0.0); // identity / original frame

            try
            {
                var oldMbr = RotatingCalipers.MinimumBoundingRectangle(old.Outer.Points);
                var newMbr = RotatingCalipers.MinimumBoundingRectangle(@new.Outer.Points);

                // A near-square MBR has unstable angle selection; the 90-degree
                // family plus identity covers the practical seeds either way.
                var delta = oldMbr.Angle - newMbr.Angle;
                for (var k = 0; k < 4; k++)
                    Add(delta + k * System.Math.PI / 2);

                // Swap W/H orientation: MBR angle may report the long edge either way.
                for (var k = 0; k < 4; k++)
                    Add(delta + System.Math.PI / 2 + k * System.Math.PI / 2);
            }
            catch (Exception)
            {
                // MBR needs a real polygon; fall back to identity-only seeds.
            }

            // Translation seed: align outer-sample centroids for every rotation.
            var oldC = Centroid(old.Outer.Samples);
            var newC = Centroid(@new.Outer.Samples);
            for (var i = 0; i < seeds.Count; i++)
            {
                var rotated = Rotate(newC, seeds[i].Item1);
                seeds[i] = (seeds[i].Item1, oldC - rotated);
            }
            return seeds;
        }

        private static List<Candidate> SearchStarts(
            Side old,
            Side @new,
            List<(double Rotation, Vector Translation)> seeds,
            AlignmentOptions options,
            bool reflectNew,
            CancellationToken cancellationToken
        )
        {
            var candidates = new List<Candidate>();
            Func<double, double, (double X, double Y)> mirror = reflectNew
                ? static (double x, double y) => (x, -y)
                : static (double x, double y) => (x, y);

            foreach (var seed in seeds)
            {
                // work-limit guard doubles as the cancellation checkpoint
                if (cancellationToken.IsCancellationRequested)
                    break;

                var candidate = FitIcp(
                    old,
                    @new,
                    seed.Rotation,
                    seed.Translation,
                    options,
                    mirror,
                    false,
                    cancellationToken
                );
                if (candidate != null)
                    candidates.Add(candidate);
            }
            candidates.Sort((a, b) => a.OuterRms.CompareTo(b.OuterRms));
            return candidates;
        }

        private static Candidate FitIcp(
            Side old,
            Side @new,
            double rotation,
            Vector translation,
            AlignmentOptions options,
            Func<double, double, (double X, double Y)> preMirror,
            bool includeHoles,
            CancellationToken cancellationToken
        )
        {
            var n = @new.Outer.Samples.Length;
            var totalNew = includeHoles ? @new.TotalSamples : n;
            var positions = new Vector[totalNew];

            void LoadPositions()
            {
                var idx = 0;
                for (var i = 0; i < @new.Outer.Samples.Length; i++)
                {
                    var p = @new.Outer.Samples[i].Position;
                    var m = preMirror(p.X, p.Y);
                    positions[idx++] = Rotate(new Vector(m.X, m.Y), rotation) + translation;
                }
                if (!includeHoles)
                    return;
                for (var h = 0; h < @new.Holes.Count; h++)
                    for (var i = 0; i < @new.Holes[h].Samples.Length; i++)
                    {
                        var p = @new.Holes[h].Samples[i].Position;
                        var m = preMirror(p.X, p.Y);
                        positions[idx++] = Rotate(new Vector(m.X, m.Y), rotation) + translation;
                    }
            }

            var holeWeight = 0.25;
            var oldOuterSegments = old.Outer.Segments;
            var oldHoleSegments = AllHoleSegments(old);
            var iterations = 0;
            var converged = false;
            var previousRms = double.PositiveInfinity;

            var residuals = new double[totalNew];
            var targets = new Vector[totalNew];
            var weights = new double[totalNew];

            for (var iter = 0; iter < options.MaxIterations; iter++)
            {
                iterations = iter + 1;
                if (cancellationToken.IsCancellationRequested)
                    break;

                LoadPositions();

                // Correspondences: closest bounded target segment per sample.
                // Outer-to-outer and hole-to-hole candidates stay distinct: a hole
                // sample never projects onto the outer perimeter when hole targets
                // exist, so a moved hole is trimmed instead of dragging the ring.
                var matched = 0;
                var outerMatched = 0;
                var outerCount = n;
                for (var i = 0; i < totalNew; i++)
                {
                    var isOuter = i < n;
                    var segments = isOuter
                        ? oldOuterSegments
                        : oldHoleSegments.Length > 0
                            ? oldHoleSegments
                            : oldOuterSegments;
                    var pt = positions[i];
                    if (!TryClosestSegment(pt, segments, out var closest))
                    {
                        residuals[i] = double.PositiveInfinity;
                        continue;
                    }

                    targets[i] = closest;
                    weights[i] = isOuter ? 1.0 : holeWeight;
                    residuals[i] = pt.DistanceTo(closest);
                    matched++;
                    if (isOuter && residuals[i] <= options.OutlierDistance)
                        outerMatched++;
                }

                if (matched < System.Math.Max(4, options.MinSamples) || outerMatched < System.Math.Max(3, outerCount / 4))
                    return null; // insufficient support for this basin

                // Trim: drop the worst correspondences, never below half the data.
                var keep = System.Math.Max(matched / 2, (int)System.Math.Ceiling(matched * (1.0 - options.TrimFraction)));
                var order = Enumerable
                    .Range(0, totalNew)
                    .Where(i => !double.IsInfinity(residuals[i]))
                    .OrderBy(i => residuals[i])
                    .Take(keep)
                    .ToArray();
                if (order.Length < 4)
                    return null;

                // Weighted rigid least squares to the projected target points.
                if (!SolveRotationTranslation(order, positions, targets, weights, out var dTheta, out var dT))
                    return null; // nonfinite update

                rotation += dTheta;
                translation = Rotate(translation, dTheta) + dT;

                var rms = System.Math.Sqrt(order.Average(i => residuals[i] * residuals[i]));

                var dAngle = System.Math.Abs(dTheta);
                var dTrans = dT.DistanceTo(new Vector());
                if (
                    (dAngle < options.RotationEpsilon && dTrans < options.TranslationEpsilon)
                    || System.Math.Abs(previousRms - rms) < 1e-12
                )
                {
                    converged = true;
                    previousRms = rms;
                    break;
                }
                previousRms = rms;
            }

            // full-data diagnostics at the converged pose: the ranking metric is
            // the trimmed outer-boundary residual; hole evidence is reported
            // separately and only used to break near-ties.
            LoadPositions();
            var outerTrimSum = 0.0;
            var outerTrimCount = 0;
            var holeTrimSum = 0.0;
            var holeTrimCount = 0;
            for (var i = 0; i < totalNew; i++)
            {
                var isOuter = i < n;
                var pt = positions[i];
                var segs = isOuter ? oldOuterSegments : oldHoleSegments.Length > 0 ? oldHoleSegments : oldOuterSegments;
                if (!TryClosestSegment(pt, segs, out var closest))
                    continue;
                var dist = pt.DistanceTo(closest);
                if (dist > options.OutlierDistance)
                    continue; // changed/unmatched span, not fit error
                if (isOuter)
                {
                    outerTrimSum += dist * dist;
                    outerTrimCount++;
                }
                else
                {
                    holeTrimSum += dist * dist;
                    holeTrimCount++;
                }
            }
            if (outerTrimCount < 3)
                return null;

            var combinedCount = outerTrimCount + holeTrimCount;
            var combinedSum = outerTrimSum + holeTrimSum;
            return new Candidate
            {
                Rotation = NormalizeAngle(rotation),
                Translation = translation,
                OuterRms = System.Math.Sqrt(outerTrimSum / outerTrimCount),
                CombinedRms = combinedCount > 0 ? System.Math.Sqrt(combinedSum / combinedCount) : double.NaN,
                Iterations = iterations,
                Converged = converged,
            };
        }

        private static Line[] AllHoleSegments(Side old)
        {
            var list = new List<Line>();
            foreach (var h in old.Holes)
                list.AddRange(h.Segments);
            return list.ToArray();
        }

        private static Line[] AllOldSegments(Side old)
        {
            var list = new List<Line>(old.Outer.Segments);
            foreach (var h in old.Holes)
                list.AddRange(h.Segments);
            return list.ToArray();
        }

        private static bool TryClosestSegment(Vector pt, Line[] segments, out Vector closest)
        {
            closest = Vector.Invalid;
            var bestDistance = double.PositiveInfinity;
            foreach (var segment in segments)
            {
                var candidate = segment.ClosestPointTo(pt);
                var d = pt.DistanceTo(candidate);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    closest = candidate;
                }
            }
            return !double.IsInfinity(bestDistance);
        }

        private static bool SolveRotationTranslation(
            int[] order,
            Vector[] source,
            Vector[] target,
            double[] weights,
            out double dTheta,
            out Vector dTranslation
        )
        {
            dTheta = 0;
            dTranslation = Vector.Invalid;

            var wSum = 0.0;
            var qs = new Vector();
            var ts = new Vector();
            foreach (var i in order)
            {
                var w = weights[i];
                wSum += w;
                qs += source[i] * w;
                ts += target[i] * w;
            }
            if (wSum <= 0)
                return false;
            var cq = qs * (1.0 / wSum);
            var ct = ts * (1.0 / wSum);

            var covXY = 0.0;
            var covXX = 0.0;
            foreach (var i in order)
            {
                var w = weights[i];
                var dq = source[i] - cq;
                var dt = target[i] - ct;
                covXY += w * (dq.X * dt.Y - dq.Y * dt.X);
                covXX += w * (dq.X * dt.X + dq.Y * dt.Y);
            }
            if (!(covXX > 0) || double.IsNaN(covXY) || double.IsNaN(covXX))
                return false;

            dTheta = System.Math.Atan2(covXY, covXX);
            if (double.IsNaN(dTheta) || double.IsInfinity(dTheta))
                return false;

            var r = Rotate(cq, dTheta);
            dTranslation = ct - r;
            return !double.IsNaN(dTranslation.X) && !double.IsNaN(dTranslation.Y);
        }

        // ---------- result assembly ----------

        private static AlignmentResult BuildResult(
            Side old,
            Side @new,
            List<Candidate> candidates,
            List<Candidate> reflectedCandidates,
            AlignmentOptions options
        )
        {
            // Stage 2: fold in retained hole evidence only where it can actually
            // distinguish competing perimeter fits. Candidates whose outer fit is
            // within the tie band of the best are re-scored with hole evidence at
            // reduced weight; a moved hole raises those candidates' combined RMS
            // without dragging the (unchanged) perimeter pose itself.
            var hasHoles = @new.Holes.Count > 0 && old.Holes.Count > 0;
            var band = System.Math.Max(10 * options.FlattenTolerance, 0.05 * options.SamplingSpacing);
            var bestOuter = candidates.Min(c => c.OuterRms);
            var tied = candidates.Where(c => c.OuterRms <= bestOuter + band).ToList();

            if (hasHoles)
            {
                foreach (var c in tied)
                {
                    var refined = FitIcp(
                        old,
                        @new,
                        c.Rotation,
                        c.Translation,
                        options,
                        static (double x, double y) => (x, y),
                        true,
                        CancellationToken.None
                    );
                    if (refined != null)
                    {
                        c.CombinedRms = refined.CombinedRms;
                        c.Iterations += refined.Iterations;
                    }
                }
            }

            // Winner: lowest combined RMS among the outer-tied set (holes can only
            // reorder candidates inside the tie band), preferring the smaller outer
            // residual when hole evidence is absent or identical.
            var best = tied
                .OrderBy(c => hasHoles && !double.IsNaN(c.CombinedRms) ? c.CombinedRms : c.OuterRms)
                .ThenBy(c => c.OuterRms)
                .First();

            var reasons = AlignmentReasons.None;
            if (!best.Converged)
                reasons |= AlignmentReasons.FailedConvergence;

            // Pose ambiguity: distinct converged poses that fit near-equally on the
            // data used to pick them (outer + holes when present) but move the
            // geometry differently. A symmetric rectangle cannot yield a uniquely
            // recoverable 180-degree orientation without other evidence.
            var ambiguityCandidates = new List<Candidate>
            {
                best,
            };
            foreach (var other in tied)
                if (!ReferenceEquals(other, best))
                    ambiguityCandidates.Add(other);

            // plus the winner's symmetry family: a 180-degree reversal about the
            // outer centroid re-fit from the flipped start
            var outerCenter = Centroid(old.Outer.Samples);
            foreach (var flip in new[] { System.Math.PI, System.Math.PI / 2, -System.Math.PI / 2 })
            {
                var anchor = Rotate(-outerCenter, flip) + outerCenter;
                var probe = new Candidate
                {
                    Rotation = NormalizeAngle(best.Rotation + flip),
                    Translation = Rotate(best.Translation, flip) + anchor,
                };
                var refined = FitIcp(
                    old,
                    @new,
                    probe.Rotation,
                    probe.Translation,
                    options,
                    static (double x, double y) => (x, y),
                    hasHoles,
                    CancellationToken.None
                );
                if (refined != null)
                    ambiguityCandidates.Add(refined);
            }

            var scored = ambiguityCandidates
                .Select(c => (
                    Pose: c,
                    Metric: hasHoles && !double.IsNaN(c.CombinedRms) ? c.CombinedRms : c.OuterRms
                ))
                .ToList();
            var bestMetric = scored.Min(s => s.Metric);
            var equivalents = 0;
            var distinctAmbiguous = false;
            foreach (var (pose, metric) in scored)
            {
                if (metric > bestMetric + band)
                    continue;
                equivalents++;
                if (AppliesDifferently(pose, best, @new, options))
                    distinctAmbiguous = true;
            }
            if (distinctAmbiguous && equivalents > 1)
                reasons |= AlignmentReasons.UnresolvedAlternatives;

            // Bidirectional coverage and residual gate. These are review reasons,
            // not a calibrated envelope: no threshold here licenses skipping the
            // operator overlay.
            var (newToOld, oldToNew) = Coverage(old, @new, best, options);
            var supportGate = 0.90; // design choice pending calibration
            var residualGate = System.Math.Max(4 * options.FlattenTolerance, 0.5 * options.SamplingSpacing);
            if (best.OuterRms > residualGate || System.Math.Min(newToOld, oldToNew) < supportGate)
                reasons |= AlignmentReasons.SignificantBoundaryChange;
            if (oldToNew < 0.5 || newToOld < 0.5)
                reasons |= AlignmentReasons.InsufficientSupport;

            // Reflection diagnostics: reported, never applied (user decision D3).
            var reflectedBand = System.Math.Max(10 * options.FlattenTolerance, 0.1 * options.SamplingSpacing);
            var reflectedBest = reflectedCandidates.Count > 0 ? reflectedCandidates.Min(c => c.OuterRms) : double.PositiveInfinity;
            if (reflectedBest <= best.OuterRms + reflectedBand)
                reasons |= AlignmentReasons.ReflectionUncertain;

            double? iou = null;
            try
            {
                iou = RegionIoU(old, @new, best, options);
            }
            catch (Exception)
            {
                iou = null; // diagnostic only; never gates anything
            }

            var quantiles = ResidualQuantiles(old, @new, best, options);

            return new AlignmentResult
            {
                Converged = best.Converged,
                Rotation = best.Rotation,
                Translation = best.Translation,
                Reflection = false,
                Reasons = reasons,
                ResidualRms = best.OuterRms,
                ResidualP50 = quantiles.P50,
                ResidualP90 = quantiles.P90,
                NewToOldCoverage = newToOld,
                OldToNewCoverage = oldToNew,
                EquivalentCandidateCount = equivalents,
                Iterations = best.Iterations,
                NewSampleCount = @new.TotalSamples,
                OldSampleCount = old.TotalSamples,
                DiagnosticIoU = iou,
                FailureMessage = (reasons & (AlignmentReasons.FailedConvergence | AlignmentReasons.InsufficientSupport)) != 0
                    ? "Automatic alignment is not trustworthy for this pair; use the manual overlay."
                    : null,
            };
        }

        private static bool AppliesDifferently(
            Candidate a,
            Candidate best,
            Side @new,
            AlignmentOptions options
        )
        {
            // Apply both poses to the new outer samples; equivalent poses leave the
            // geometry where the other pose put it (max displacement below half the
            // sampling spacing).
            var limit = options.SamplingSpacing * 0.5;
            foreach (var s in @new.Outer.Samples)
            {
                var pa = Rotate(s.Position, a.Rotation) + a.Translation;
                var pb = Rotate(s.Position, best.Rotation) + best.Translation;
                if (pa.DistanceTo(pb) > limit)
                    return true;
            }
            return false;
        }

        private static (double NewToOld, double OldToNew) Coverage(
            Side old,
            Side @new,
            Candidate pose,
            AlignmentOptions options
        )
        {
            var oldSegments = AllOldSegments(old);
            var spanBound = System.Math.Min(old.SpanDistance, @new.SpanDistance);

            var newMatched = 0;
            var newTotal = 0;
            foreach (var s in AllNewSamples(@new))
            {
                var p = Rotate(s.Position, pose.Rotation) + pose.Translation;
                newTotal++;
                if (TryClosestSegment(p, oldSegments, out var c) && p.DistanceTo(c) <= spanBound)
                    newMatched++;
            }

            // the reverse direction must compare OLD samples against the REVISED
            // geometry posed into the OLD frame, not its local frame
            var posedNewSegments = new Line[AllNewSegments(@new).Length];
            var posed = AllNewSegments(@new);
            for (var i = 0; i < posed.Length; i++)
                posedNewSegments[i] = new Line(
                    Rotate(posed[i].StartPoint, pose.Rotation) + pose.Translation,
                    Rotate(posed[i].EndPoint, pose.Rotation) + pose.Translation
                );

            var oldMatched = 0;
            var oldSamples = AllOldSamples(old);
            foreach (var s in oldSamples)
            {
                if (TryClosestSegment(s.Position, posedNewSegments, out var c) && s.Position.DistanceTo(c) <= spanBound)
                    oldMatched++;
            }

            return (
                newTotal > 0 ? (double)newMatched / newTotal : 0,
                oldSamples.Length > 0 ? (double)oldMatched / oldSamples.Length : 0
            );
        }

        private static (double P50, double P90) ResidualQuantiles(
            Side old,
            Side @new,
            Candidate pose,
            AlignmentOptions options
        )
        {
            var oldSegments = AllOldSegments(old);
            var distances = new List<double>();
            foreach (var s in AllNewSamples(@new))
            {
                var p = Rotate(s.Position, pose.Rotation) + pose.Translation;
                if (TryClosestSegment(p, oldSegments, out var c))
                    distances.Add(p.DistanceTo(c));
            }
            if (distances.Count == 0)
                return (double.NaN, double.NaN);
            distances.Sort();
            return (Quantile(distances, 0.5), Quantile(distances, 0.9));
        }

        private static double Quantile(List<double> sorted, double q)
        {
            var pos = (sorted.Count - 1) * q;
            var lo = (int)System.Math.Floor(pos);
            var hi = (int)System.Math.Ceiling(pos);
            return lo == hi ? sorted[lo] : sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
        }

        private static double? RegionIoU(Side old, Side @new, Candidate pose, AlignmentOptions options)
        {
            var oldRegion = ToMaterialPaths(old.Outer, old.Holes, Vector.Zero, 0);
            var newRegion = ToMaterialPaths(@new.Outer, @new.Holes, pose.Translation, pose.Rotation);
            if (oldRegion == null || newRegion == null)
                return null;

            var inter = Clipper.Intersect(oldRegion, newRegion, FillRule.NonZero, ClipperBridge.Precision);
            var union = Clipper.Union(oldRegion, newRegion, FillRule.NonZero, ClipperBridge.Precision);
            var interArea = System.Math.Abs(Clipper.Area(inter));
            var unionArea = Clipper.Area(union);
            if (!(unionArea > 0))
                return null;
            var iou = interArea / unionArea;
            // Bounded diagnostic: values outside [0,1] beyond rounding mean the
            // region arithmetic is wrong for this input; report nothing rather
            // than clamping an invalid measurement.
            if (iou < -1e-6 || iou > 1 + 1e-6)
                return null;
            return System.Math.Clamp(iou, 0, 1);
        }

        private static PathsD ToMaterialPaths(Ring outer, List<Ring> holes, Vector translation, double rotation)
        {
            PathsD paths = new(1 + holes.Count);
            var outerPath = ToPath(outer.Points, translation, rotation, true);
            if (outerPath == null)
                return null;
            paths.Add(outerPath);
            foreach (var hole in holes)
            {
                var p = ToPath(hole.Points, translation, rotation, false);
                if (p == null)
                    return null;
                paths.Add(p);
            }
            return paths;
        }

        private static PathD ToPath(Vector[] points, Vector translation, double rotation, bool positive)
        {
            var path = new PathD(points.Length);
            foreach (var p in points)
            {
                var q = Rotate(p, rotation) + translation;
                if (double.IsNaN(q.X) || double.IsNaN(q.Y))
                    return null;
                path.Add(new PointD(q.X, q.Y));
            }
            if (path.Count < 3)
                return null;
            if (Clipper.IsPositive(path) != positive)
                path.Reverse();
            return path;
        }

        // ---------- small helpers ----------

        private static IEnumerable<ContourSample> AllNewSamples(Side side)
        {
            foreach (var s in side.Outer.Samples)
                yield return s;
            foreach (var h in side.Holes)
                foreach (var s in h.Samples)
                    yield return s;
        }

        private static ContourSample[] AllOldSamples(Side side) => AllNewSamples(side).ToArray();

        private static Line[] AllNewSegments(Side side)
        {
            var list = new List<Line>(side.Outer.Segments);
            foreach (var h in side.Holes)
                list.AddRange(h.Segments);
            return list.ToArray();
        }

        private static Vector Centroid(ContourSample[] samples)
        {
            var sum = new Vector();
            foreach (var s in samples)
                sum += s.Position;
            return samples.Length > 0 ? sum * (1.0 / samples.Length) : new Vector();
        }

        private static Vector Rotate(Vector v, double angle)
        {
            var cos = System.Math.Cos(angle);
            var sin = System.Math.Sin(angle);
            return new Vector(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
        }

        private static double NormalizeAngle(double angle)
        {
            var twoPi = 2 * System.Math.PI;
            var a = angle % twoPi;
            if (a > System.Math.PI)
                a -= twoPi;
            if (a < -System.Math.PI)
                a += twoPi;
            return a;
        }

        private static AlignmentResult Failure(AlignmentReasons reason, string message) =>
            new()
            {
                Converged = false,
                Reasons = reason,
                FailureMessage = message ?? "Alignment refused; use the manual overlay.",
            };
    }
}
