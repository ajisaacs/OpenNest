using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Sequencing
{
    /// <summary>Applies a sequencer's exit-first route as the plate's cutting order.</summary>
    public static class PlateSequencing
    {
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
                            || CrossesBounds(definition, part.BoundingBox, bounds))
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

        private static bool CrossesBounds(CutOff cutOff, Box part, Box plate)
        {
            var vertical = cutOff.Axis == CutOffAxis.Vertical;
            var position = vertical ? cutOff.Position.X : cutOff.Position.Y;
            var acrossMin = vertical ? part.Left : part.Bottom;
            var acrossMax = vertical ? part.Right : part.Top;
            var alongMin = vertical ? part.Bottom : part.Left;
            var alongMax = vertical ? part.Top : part.Right;
            var start = cutOff.StartLimit ?? (vertical ? plate.Bottom : plate.Left);
            var end = cutOff.EndLimit ?? (vertical ? plate.Top : plate.Right);

            // Use the nominal line, not its trimmed cutting segments (which
            // deliberately skip the parts). Bounds conservatively include edge
            // contacts and concave recesses; limits prevent unrelated dependencies
            // beyond the cutoff's span. Negative coordinates need no special case.
            return !(position < acrossMin - Tolerance.Epsilon
                || position > acrossMax + Tolerance.Epsilon
                || System.Math.Max(start, end) < alongMin - Tolerance.Epsilon
                || System.Math.Min(start, end) > alongMax + Tolerance.Epsilon);
        }
    }
}
