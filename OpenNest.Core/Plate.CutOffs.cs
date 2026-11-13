using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Collections;
using OpenNest.Geometry;

namespace OpenNest
{
    public partial class Plate
    {
        /// <summary>
        /// The cut-off lines defined on this plate.
        /// </summary>
        public ObservableList<CutOff> CutOffs { get; set; }

        /// <summary>
        /// A copy of the settings the cut-offs were last regenerated with, or null before the
        /// first regeneration. Saved with the nest, so reopening it rebuilds the same cut-off
        /// geometry rather than geometry from built-in defaults.
        /// </summary>
        public CutOffSettings CutOffSettings { get; set; }

        /// <summary>
        /// Regenerates all cut-off drawings and materializes each of their trimmed segments as
        /// a part. Existing cut-off parts are removed first, then each regenerated segment goes
        /// back to the place in the cut sequence (<see cref="Parts"/> order) of the saved segment
        /// of its cut-off it overlaps most, so a part drag or cut-off move doesn't send it to the
        /// end of the sequence; a segment with no saved counterpart follows the segment before
        /// it. New cut-offs are added at the end.
        /// </summary>
        public void RegenerateCutOffs(CutOffSettings settings)
        {
            var saved = new Dictionary<CutOff, List<(int Index, double Start, double End)>>();
            var places = new List<int>();
            for (var i = Parts.Count - 1; i >= 0; i--)
            {
                if (!Parts[i].BaseDrawing.IsCutOff)
                    continue;

                places.Add(i);
                var cutoff = CutOffs.FirstOrDefault(c => ReferenceEquals(c.Drawing, Parts[i].BaseDrawing));
                if (cutoff != null)
                {
                    if (!saved.TryGetValue(cutoff, out var segments))
                        saved[cutoff] = segments = new List<(int, double, double)>();
                    var extent = CutOffSegments.Extent(Parts[i], cutoff.Axis == CutOffAxis.Vertical);
                    segments.Add((i, extent?.Start ?? double.NaN, extent?.End ?? double.NaN));
                }

                Parts.RemoveAt(i);
            }

            RegenerateCutOffs(settings, (cutoff, extents) =>
            {
                if (!saved.TryGetValue(cutoff, out var segments))
                    return null;
                var indices = new int[extents.Count];
                var previous = segments.Min(s => s.Index);
                for (var k = 0; k < extents.Count; k++)
                {
                    var (start, end) = extents[k];
                    var best = -1;
                    var bestOverlap = 0.0;
                    foreach (var (index, savedStart, savedEnd) in segments)
                    {
                        var overlap = System.Math.Min(end, savedEnd) - System.Math.Max(start, savedStart);
                        if (overlap > bestOverlap)
                        {
                            best = index;
                            bestOverlap = overlap;
                        }
                    }
                    indices[k] = previous = best >= 0 ? best : previous;
                }
                return indices;
            }, _ => places);
        }

        /// <summary>
        /// Regenerates all cut-off drawings and materializes each of their trimmed segments as a
        /// part, placing a cut-off's segments consecutively from its index in
        /// <paramref name="sequence"/> (its place in <see cref="Parts"/> order). Cut-offs missing
        /// from it are added at the end. Callers must remove existing cut-off parts first.
        /// </summary>
        public void RegenerateCutOffs(CutOffSettings settings, IReadOnlyDictionary<CutOff, int> sequence) =>
            RegenerateCutOffs(settings, null, sequence);

        /// <summary>
        /// As <see cref="RegenerateCutOffs(CutOffSettings, IReadOnlyDictionary{CutOff, int})"/>,
        /// but each cut-off lists one index per segment in ascending order along its axis. A list
        /// whose length differs from the regenerated segment count places the segments
        /// consecutively at its smallest index.
        /// </summary>
        public void RegenerateCutOffs(CutOffSettings settings, IReadOnlyDictionary<CutOff, IReadOnlyList<int>> sequence) =>
            RegenerateCutOffs(settings, sequence, null);

        /// <summary>
        /// Places each cut-off's regenerated segments where its saved segments were, among the
        /// parts saved around them: <paramref name="places"/> lists every saved segment's place;
        /// <paramref name="firstPlaces"/> gives only the first, for files that recorded no more
        /// (their segments were consecutive, but how many is unknown). Settings other than the
        /// saved ones can regenerate a different number of segments; the parts keep their
        /// places relative to the cut-offs either way. Where only a first place is known the
        /// count is a guess, so a cut-off that splits the sheet goes last, as sequencing puts it.
        /// </summary>
        public void RegenerateCutOffs(CutOffSettings settings, IReadOnlyDictionary<CutOff, IReadOnlyList<int>> places,
            IReadOnlyDictionary<CutOff, int> firstPlaces) =>
            RegenerateCutOffs(settings, (cutoff, extents) =>
            {
                if (places != null && places.TryGetValue(cutoff, out var indices) && indices is { Count: > 0 })
                {
                    if (indices.Count != extents.Count)
                        return Enumerable.Repeat(indices.Min(), extents.Count).ToArray();
                    // Program order can run toward the origin; the file lists segments ascending.
                    var ascending = Enumerable.Range(0, extents.Count).OrderBy(k => extents[k].Start).ToArray();
                    var result = new int[extents.Count];
                    for (var rank = 0; rank < ascending.Length; rank++)
                        result[ascending[rank]] = indices[rank];
                    return result;
                }
                return firstPlaces != null && firstPlaces.TryGetValue(cutoff, out var first)
                    ? Enumerable.Repeat(first, extents.Count).ToArray() : null;
            }, counts =>
            {
                var saved = new HashSet<int>();
                foreach (var list in places?.Values ?? [])
                    saved.UnionWith(list ?? []);
                if (firstPlaces == null)
                    return saved;
                // A cut-off saved by its first place alone held consecutive places, as many as it
                // has segments now unless that runs into the next cut-off's first place.
                var starts = saved.Concat(firstPlaces.Values).Distinct().Order().ToArray();
                foreach (var (cutoff, first) in firstPlaces)
                {
                    var next = starts.FirstOrDefault(start => start > first, int.MaxValue);
                    var count = System.Math.Max(1, System.Math.Min(counts.GetValueOrDefault(cutoff, 1), next - first));
                    saved.UnionWith(Enumerable.Range(first, count));
                }
                return saved;
            }, cutoff => firstPlaces != null && firstPlaces.ContainsKey(cutoff));

        // sequence gives a cut-off's saved place for each segment in program order, from its
        // segments' extents along its axis (NaN when unmeasurable), or null to append them.
        // savedPlaces gives every place cut-off segments held in the saved order, from each
        // cut-off's regenerated segment count; the parts held the others, in their order.
        // separateLast says which cut-offs go after everything when they split the sheet.
        private void RegenerateCutOffs(CutOffSettings settings,
            Func<CutOff, IReadOnlyList<(double Start, double End)>, IReadOnlyList<int>> sequence,
            Func<IReadOnlyDictionary<CutOff, int>, IEnumerable<int>> savedPlaces,
            Func<CutOff, bool> separateLast = null)
        {
            var cache = BuildPerimeterCache(this);
            var sheet = BoundingBox(includeParts: false);
            var placed = new List<(int Index, int Order, Part Part)>();
            var separating = new List<Part>();
            var counts = new Dictionary<CutOff, int>();
            var partCount = Parts.Count;
            CutOffSettings = settings?.Copy();

            // Regenerate and materialize each cut-off's segments
            foreach (var cutoff in CutOffs)
            {
                cutoff.Regenerate(this, settings, cache);

                var programs = cutoff.SegmentPrograms();
                if (programs.Count == 0)
                    continue;

                var vertical = cutoff.Axis == CutOffAxis.Vertical;
                var parts = programs.Select(program => new Part(cutoff.Drawing, program)).ToArray();
                var extents = parts.Select(part => CutOffSegments.Extent(part, vertical) ?? (double.NaN, double.NaN)).ToArray();
                var indices = sequence?.Invoke(cutoff, extents);
                counts[cutoff] = parts.Length;
                for (var k = 0; k < parts.Length; k++)
                {
                    // A line splitting the sheet comes after every part and every other cut-off.
                    if (separateLast?.Invoke(cutoff) == true && CutOff.SeparatesSheet(parts[k], cutoff, sheet))
                        separating.Add(parts[k]);
                    else if (indices != null && k < indices.Count)
                        placed.Add((indices[k], placed.Count, parts[k]));
                    else
                        Parts.Add(parts[k]);
                }
            }

            // Saved places count every cut-off segment then on the plate, which may now be more or
            // fewer. A segment goes after the parts saved before it and the segments placed before
            // it, so a changed count never shifts a later cut-off or part. Segments sharing a place
            // keep their program order.
            var saved = (savedPlaces?.Invoke(counts) ?? []).Distinct().Order().ToArray();
            var inserted = 0;
            foreach (var (index, _, part) in placed.OrderBy(p => p.Index).ThenBy(p => p.Order))
            {
                var savedBefore = Array.BinarySearch(saved, index);
                savedBefore = savedBefore < 0 ? ~savedBefore : savedBefore;
                var partsBefore = System.Math.Clamp(index - savedBefore, 0, partCount);
                Parts.Insert(partsBefore + inserted++, part);
            }
            foreach (var part in separating)
                Parts.Add(part);
        }

        /// <summary>
        /// Builds a dictionary mapping each non-cut-off part to its perimeter entity.
        /// Closed shapes use ShapeProfile; open contours fall back to ConvexHull.
        /// </summary>
        public static Dictionary<Part, Geometry.Entity> BuildPerimeterCache(Plate plate)
        {
            var cache = new Dictionary<Part, Geometry.Entity>();

            foreach (var part in plate.Parts)
            {
                if (part.BaseDrawing.IsCutOff)
                    continue;

                cache[part] = BuildPartPerimeter(part);
            }

            return cache;
        }

        /// <summary>
        /// Converts one part's own program into its placed perimeter entity (closed
        /// profile, or a convex hull fallback for a genuinely open contour). This is
        /// the same conversion <see cref="BuildPerimeterCache"/> runs per part; callers
        /// without a pre-built cache (e.g. a direct, uncached <see cref="CutOff.Regenerate"/>
        /// call) use it to get the same conservative, converted-material envelope instead
        /// of trusting the part's cached bounding box alone.
        /// </summary>
        public static Geometry.Entity BuildPartPerimeter(Part part)
        {
            // Null is the conservative answer: the cut-off then excludes the part's whole
            // bounding-box span. Only malformed geometry earns it; anything else propagates.
            if (part?.Program == null)
                return null;

            try
            {
                var entities = Converters
                    .ConvertProgram.ToGeometry(part.Program)
                    .Where(e => SpecialLayers.IsMaterial(e.Layer))
                    .ToList();

                if (entities.Count == 0)
                    return null;

                // Leads are cutting paths in scrap, not boundary edges. Chaining them
                // into the outline can make a closed part appear open and replace its
                // recesses with a convex hull. A cut-off may cross a lead, so the open-
                // contour hull also leaves them out: hulling a lead's pierce point would
                // stop cut-offs far short of the part.
                var outline = entities.Where(e => e.Layer != SpecialLayers.Leadin &&
                    e.Layer != SpecialLayers.Leadout).ToList();
                if (outline.Count == 0)
                    return null;
                var shapes = Geometry.ShapeBuilder.GetShapes(outline);
                if (shapes.Count == 0)
                    return null;
                var profile = Geometry.ShapeProfile.FromShapes(shapes);

                if (profile.Perimeter.IsClosed())
                {
                    var perimeter = profile.Perimeter;
                    perimeter.Offset(part.Location);
                    return perimeter;
                }

                var points = outline.CollectPoints();
                if (points.Count < 3)
                    return null;

                var hull = Geometry.ConvexHull.Compute(points);
                hull.Offset(part.Location);
                return hull;
            }
            catch (Exception e) when (Diagnostics.PlateOverlapAnalyzer.IsGeometryFailure(e))
            {
                return null;
            }
        }
    }
}
