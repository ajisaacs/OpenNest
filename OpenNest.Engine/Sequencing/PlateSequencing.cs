using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC.CuttingStrategy;
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
        /// Moves the given cutoffs' parts to where the cutoff rules need them while every other
        /// part and cutoff keeps its position: just before the first part each nominal line
        /// crosses, or last when it splits the whole sheet. One that crosses nothing stays put,
        /// unless it would follow a sheet-separating cutoff.
        /// </summary>
        public static void PlaceCutOffs(Plate plate, IEnumerable<CutOff> definitions)
        {
            var bounds = plate.BoundingBox(includeParts: false);
            var moving = definitions
                .Select(definition => (Definition: definition, Part: plate.Parts.FirstOrDefault(part =>
                    ReferenceEquals(part.BaseDrawing, definition.Drawing))))
                .Where(item => item.Part != null).ToList();
            if (moving.Count == 0)
                return;

            var existing = new Dictionary<Drawing, CutOff>(ReferenceEqualityComparer.Instance);
            foreach (var cutOff in plate.CutOffs)
                existing[cutOff.Drawing] = cutOff;
            var order = plate.Parts.ToList();
            var separating = new List<Part>();
            foreach (var (definition, part) in moving)
            {
                if (CutOff.SeparatesSheet(part, definition, bounds))
                {
                    order.Remove(part);
                    separating.Add(part);
                    continue;
                }
                var target = order.FindIndex(other => !other.BaseDrawing.IsCutOff
                    && CuttingDependencyGraph.CutOffCrosses(definition.Axis, definition.Position,
                        definition.StartLimit, definition.EndLimit, other.BoundingBox, bounds));
                if (target < 0)
                    target = order.FindIndex(other => other.BaseDrawing.IsCutOff && !moving.Any(m => ReferenceEquals(m.Part, other))
                        && existing.TryGetValue(other.BaseDrawing, out var cut) && CutOff.SeparatesSheet(other, cut, bounds));
                if (target < 0 || target > order.IndexOf(part))
                    continue;
                order.Remove(part);
                order.Insert(target, part);
            }
            order.AddRange(separating);

            // Touch only the moved cutoff parts; real parts never leave the plate.
            foreach (var (_, part) in moving)
                plate.Parts.Remove(part);
            for (var index = 0; index < order.Count; index++)
            {
                if (moving.Any(item => ReferenceEquals(item.Part, order[index])))
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

            // A cutoff that splits the whole sheet goes after every part and other cutoff, so the
            // head never travels back across it. It passes through no part.
            var separating = ordered.Where(part => part.BaseDrawing.IsCutOff
                && definitions.TryGetValue(part.BaseDrawing, out var definition)
                && CutOff.SeparatesSheet(part, definition, bounds)).ToList();
            if (separating.Count > 0)
            {
                var last = new HashSet<Part>(separating, ReferenceEqualityComparer.Instance);
                ordered = ordered.Where(part => !last.Contains(part)).ToList();
            }

            var cuts = ordered.Select((part, index) => (Part: part, Index: index))
                .Where(item => item.Part.BaseDrawing.IsCutOff).ToList();
            if (cuts.Count == 0)
                return ordered.Concat(separating).ToList();

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
                            || CuttingDependencyGraph.CutOffCrosses(definition.Axis, definition.Position,
                                definition.StartLimit, definition.EndLimit, part.BoundingBox, bounds))
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
