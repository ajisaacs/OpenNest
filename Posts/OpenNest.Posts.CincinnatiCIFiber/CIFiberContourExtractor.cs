using System;
using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Posts.CincinnatiCIFiber
{
    /// <summary>
    /// One contour (pierce + lead-in + cut path) reduced to sheet-absolute
    /// moves, ready to emit as a TF5200 N-label block.
    /// </summary>
    public sealed class CIFiberContour
    {
        /// <summary>Pierce point (sheet-absolute) — rapid target.</summary>
        public Vector Pierce { get; set; }

        /// <summary>
        /// Lead-in move to the contour start (sheet-absolute endpoint), or
        /// null when the contour begins cutting immediately at the pierce.
        /// Per TF5200 §13.2.4.1 the first motion block after G41/G42 selection
        /// must be LINEAR, so callers must reject contours whose lead-in is an
        /// arc unless the config tolerates it.
        /// </summary>
        public Motion LeadIn { get; set; }

        /// <summary>
        /// Additional lead-in layer moves after the first (e.g. the arc that
        /// follows CleanHoleLeadIn's line). Emitted before the cut layer turns
        /// on, like the sample's lead block.
        /// </summary>
        public List<Motion> LeadInExtra { get; } = new();

        /// <summary>Cutting moves (sheet-absolute) executed after the cut layer turns on.</summary>
        public List<Motion> Cuts { get; } = new();

        /// <summary>True when the contour is an external perimeter (exterior).</summary>
        public bool IsExterior { get; set; }
    }

    /// <summary>
    /// Extracts cut contours for one part in sheet coordinates.
    /// Circular holes arrive as <see cref="SubProgramCall"/>s in an incremental
    /// hole-local program; they are flattened here to sheet-absolute moves the
    /// same way <see cref="NestPolylineExtractor"/> walks them for HPGL output.
    /// Moves with <see cref="Motion.Suppressed"/> are skipped.
    /// </summary>
    public static class CIFiberContourExtractor
    {
        public static List<CIFiberContour> Extract(Part part)
        {
            if (part == null)
                throw new ArgumentNullException(nameof(part));

            var contours = new List<CIFiberContour>();
            Flatten(part.Program, part.Location, Vector.Zero, contours);
            return contours;
        }

        /// <summary>
        /// Walk one program whose coordinates are relative to (partLocation +
        /// callOffset). Sub-program calls recurse with the extra offset.
        /// A contour starts at each rapid move; moves before the first rapid
        /// form a contour whose pierce is the last move's endpoint (closed-loop
        /// continuation), matching FeatureUtils' synthetic-rapid convention.
        /// </summary>
        private static void Flatten(
            Program program,
            Vector partLocation,
            Vector subOffset,
            List<CIFiberContour> contours
        )
        {
            if (program == null)
                return;

            var origin = partLocation + subOffset;

            // Work on a clone when conversion is needed so caller programs are
            // never mutated (the CI sample inlines absolute moves).
            var codes = program.Codes;
            if (program.Mode == Mode.Incremental)
            {
                var clone = (Program)program.Clone();
                clone.Mode = Mode.Absolute;
                codes = clone.Codes;
            }

            CIFiberContour current = null;

            void Flush()
            {
                if (current != null && (current.LeadIn != null || current.Cuts.Count > 0))
                    contours.Add(current);
                current = null;
            }

            foreach (var code in codes)
            {
                if (code is Motion suppressed && suppressed.Suppressed)
                    continue;

                switch (code)
                {
                    case RapidMove rapid:
                        Flush();
                        current = new CIFiberContour { Pierce = rapid.EndPoint + origin };
                        break;

                    case SubProgramCall call:
                        // A hole call is its own contour: flatten into a temp
                        // list, then merge as one contour anchored at the call
                        // offset.
                        Flush();
                        var sub = new List<CIFiberContour>();
                        Flatten(call.Program, partLocation, subOffset + call.Offset, sub);
                        contours.AddRange(sub);
                        break;

                    case Motion motion:
                        if (current == null)
                        {
                            // Contour with no explicit rapid (program starts
                            // mid-contour): in absolute mode the implicit
                            // start position is the frame origin, which is the
                            // closure point of a closed contour = the pierce.
                            current = new CIFiberContour { Pierce = origin };
                        }

                        var absolute = (Motion)motion.Clone();
                        absolute.Offset(origin);

                        var layer = LayerOf(motion);
                        if (layer == LayerType.Leadin)
                        {
                            // First lead-in move opens the lead block; further
                            // lead-in moves (CleanHole line+arc) extend it.
                            if (current.LeadIn == null)
                                current.LeadIn = absolute;
                            else
                                current.LeadInExtra.Add(absolute);
                        }
                        else
                        {
                            // Cut, Leadout and Display all cut: OpenNest tags
                            // contour moves Display by default (see
                            // ContourCuttingStrategy.ConvertShapeToMoves),
                            // only Scribe is reliably a mark. Scribe contours
                            // are dropped by the writer when SkipScribe is on.
                            current.Cuts.Add(absolute);
                        }
                        break;
                }
            }

            Flush();
        }

        private static LayerType LayerOf(Motion motion)
        {
            return motion switch
            {
                LinearMove l => l.Layer,
                ArcMove a => a.Layer,
                _ => LayerType.Cut,
            };
        }
    }
}
