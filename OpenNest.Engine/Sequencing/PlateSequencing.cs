using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.CutOffs;
using OpenNest.Engine.CuttingPlanning;

namespace OpenNest.Engine.Sequencing
{
    /// <summary>Applies a sequencer's exit-first route as the plate's cutting order.</summary>
    public static class PlateSequencing
    {
        /// <summary>Sequences the plates present when the operation starts.</summary>
        public static void ApplyAll(IEnumerable<Plate> plates, SequenceParameters parameters)
        {
            // Reordering parts can make PlateManager replace the trailing empty
            // plate. Snapshot before applying so those events cannot invalidate
            // enumeration of the nest's live plate collection.
            foreach (var plate in plates.ToArray())
                Apply(plate, parameters);
        }

        public static void Apply(Plate plate, SequenceParameters parameters)
        {
            var sequencer = PartSequencerFactory.Create(parameters);

            // Cut-first parts (test coupons) get their own route ahead of the rest,
            // so they stay grouped instead of interleaving with the main route.
            var first = plate.Parts.Where(p => p.BaseDrawing.CutFirst).ToList();
            var rest = plate.Parts.Where(p => !p.BaseDrawing.CutFirst).ToList();
            var ordered = CutOrder(sequencer, first, plate).Concat(CutOrder(sequencer, rest, plate)).ToList();

            // Enforce dependencies AFTER reversing the exit-first route. Checking
            // the sequencer's output itself would invert the safety rule on apply.
            var cutOrder = OrderCutOffsFirst(ordered, plate);
            plate.Parts.Clear();
            foreach (var part in cutOrder)
                plate.Parts.Add(part);
        }

        /// <summary>
        /// Moves the given cutoffs' segment parts to where the cutoff rules need them while every
        /// other part and cutoff keeps its position: each segment just before the first part in
        /// its span of the nominal line (see <see cref="CutOffSegments"/>), or last when it splits
        /// off a piece of the sheet (<see cref="CutOff.Separators(Plate)"/>), before any separator it
        /// stops on. One that crosses nothing stays put, unless it would follow a separator.
        /// </summary>
        public static void PlaceCutOffs(Plate plate, IEnumerable<CutOff> definitions)
        {
            var bounds = plate.BoundingBox(includeParts: false);
            var moving = definitions
                .SelectMany(definition => CutOffSegments.Of(plate, definition)
                    .Select(segment => (Definition: definition, Segment: segment)))
                .ToList();
            if (moving.Count == 0)
                return;
            var movingParts = new HashSet<Part>(moving.Select(item => item.Segment.Part), ReferenceEqualityComparer.Instance);

            var order = plate.Parts.ToList();
            var separators = CutOff.Separators(plate);
            var separating = new List<Part>();
            foreach (var (definition, segment) in moving)
            {
                var part = segment.Part;
                if (separators.ContainsKey(part))
                {
                    order.Remove(part);
                    separating.Add(part);
                    continue;
                }
                var target = order.FindIndex(other => !other.BaseDrawing.IsCutOff
                    && CuttingDependencyGraph.CutOffCrosses(definition.Axis, definition.Position,
                        segment.SpanStart, segment.SpanEnd, other.BoundingBox, bounds, definition.Gaps));
                if (target < 0)
                    target = order.FindIndex(other => !movingParts.Contains(other) && separators.ContainsKey(other));
                if (target < 0 || target > order.IndexOf(part))
                    continue;
                order.Remove(part);
                order.Insert(target, part);
            }
            // Each moved separator goes last, but before a staying separator of an earlier round:
            // one it may stop on.
            foreach (var part in separating.OrderByDescending(part => separators[part]))
            {
                var before = order.FindIndex(other => !movingParts.Contains(other)
                    && separators.TryGetValue(other, out var round) && round < separators[part]);
                order.Insert(before < 0 ? order.Count : before, part);
            }

            // Touch only the moved cutoff parts; real parts never leave the plate.
            foreach (var part in movingParts)
                plate.Parts.Remove(part);
            for (var index = 0; index < order.Count; index++)
            {
                if (movingParts.Contains(order[index]))
                    plate.Parts.Insert(index, order[index]);
            }
        }

        private static IEnumerable<Part> CutOrder(IPartSequencer sequencer, List<Part> parts, Plate plate)
        {
            if (parts.Count == 0)
                return Enumerable.Empty<Part>();
            return sequencer.Sequence(parts, plate).Select(p => p.Part).Reverse();
        }

        internal static List<Part> OrderCutOffsFirst(List<Part> ordered, Plate plate)
        {
            var definitions = new Dictionary<Drawing, CutOff>(ReferenceEqualityComparer.Instance);
            foreach (var cutOff in plate.CutOffs)
                definitions[cutOff.Drawing] = cutOff;
            var bounds = plate.BoundingBox(includeParts: false);
            // Each cutoff part cuts one segment of its line and precedes the parts in that segment's span.
            var spans = new Dictionary<Part, (double Start, double End)>(ReferenceEqualityComparer.Instance);
            foreach (var cutOff in plate.CutOffs)
                foreach (var segment in CutOffSegments.Of(cutOff, ordered, bounds))
                    spans[segment.Part] = (segment.SpanStart, segment.SpanEnd);

            // A cutoff that splits off a piece of the sheet goes after every part and other cutoff,
            // so the head never travels back across it and the remnant's mass stays on to the end;
            // one stopping on other separators goes before them. It passes through no part.
            var separators = CutOff.Separators(ordered, plate.CutOffs, bounds);
            var separating = ordered.Where(separators.ContainsKey)
                .OrderByDescending(part => separators[part]).ToList();
            if (separating.Count > 0)
            {
                var last = new HashSet<Part>(separating, ReferenceEqualityComparer.Instance);
                ordered = ordered.Where(part => !last.Contains(part)).ToList();
            }

            var cuts = ordered.Select((part, index) => (Part: part, Index: index))
                .Where(item => item.Part.BaseDrawing.IsCutOff).ToList();
            if (cuts.Count == 0)
                return ordered.Concat(separating).ToList();
            // Cutoffs keep the route's order; within one cutoff, the segments due before the same
            // part keep the order they were regenerated in (the line's program order).
            var firstOnRoute = cuts.GroupBy(item => item.Part.BaseDrawing)
                .ToDictionary(group => group.Key, group => group.Min(item => item.Index));
            cuts = cuts.OrderBy(item => firstOnRoute[item.Part.BaseDrawing])
                .ThenBy(item => plate.Parts.IndexOf(item.Part)).ToList();

            var emitted = new bool[ordered.Count];
            var result = new List<Part>(ordered.Count);
            for (var i = 0; i < ordered.Count; i++)
            {
                var part = ordered[i];
                if (!part.BaseDrawing.IsCutOff)
                {
                    foreach (var cut in cuts)
                    {
                        if (emitted[cut.Index])
                            continue;

                        // An orphaned cutoff still must not follow potentially
                        // crossed parts when its nominal span cannot be recovered.
                        if (!definitions.TryGetValue(cut.Part.BaseDrawing, out var definition)
                            || !spans.TryGetValue(cut.Part, out var span)
                            || CuttingDependencyGraph.CutOffCrosses(definition.Axis, definition.Position,
                                span.Start, span.End, part.BoundingBox, bounds, definition.Gaps))
                        {
                            result.Add(cut.Part);
                            emitted[cut.Index] = true;
                        }
                    }
                }

                if (!emitted[i])
                {
                    result.Add(part);
                    emitted[i] = true;
                }
            }

            result.AddRange(separating);
            return result;
        }
    }
}
