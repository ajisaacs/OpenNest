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
            var ordered = sequencer.Sequence(plate.Parts.ToList(), plate)
                .Select(p => p.Part).Reverse().ToList();

            // Enforce dependencies AFTER reversing the exit-first route. Checking
            // the sequencer's output itself would invert the safety rule on apply.
            var cutOrder = OrderCutOffsFirst(ordered, plate);
            plate.Parts.Clear();
            foreach (var part in cutOrder)
                plate.Parts.Add(part);
        }

        private static List<Part> OrderCutOffsFirst(List<Part> ordered, Plate plate)
        {
            var cuts = ordered.Select((part, index) => (Part: part, Index: index))
                .Where(item => item.Part.BaseDrawing.IsCutOff).ToList();
            if (cuts.Count == 0)
                return ordered;

            var definitions = new Dictionary<Drawing, CutOff>(ReferenceEqualityComparer.Instance);
            foreach (var cutOff in plate.CutOffs)
                definitions[cutOff.Drawing] = cutOff;

            var bounds = plate.BoundingBox(includeParts: false);
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

            return result;
        }
    }
}
