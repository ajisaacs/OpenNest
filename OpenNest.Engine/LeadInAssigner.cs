using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;

namespace OpenNest.Engine
{
    public class LeadInAssigner
    {
        public IPartSequencer Sequencer { get; set; }

        /// <summary>
        /// Assigns the plate's lead-ins to every unlocked part, except parts whose drawing pins
        /// are active: those keep their programs, since only Plan Cutting cuts drawing pins, and
        /// this never overwrites a pin with the plate's settings. Returns those parts.
        /// </summary>
        public IReadOnlyList<Part> Assign(Plate plate)
        {
            var parameters = plate.CuttingParameters;
            if (parameters == null)
                return [];

            var sequenced = Sequencer.Sequence(plate.Parts.ToList(), plate);
            var exitPoint = PlateHelper.GetExitPoint(plate);

            // Pass 1: assign lead-ins to establish pierce points
            var piercePoints = AssignPass(sequenced, parameters, exitPoint, nextPiercePoints: null);

            // Pass 2: re-assign with knowledge of next part's start point
            AssignPass(sequenced, parameters, exitPoint, nextPiercePoints: piercePoints);

            return plate.Parts.Where(KeepsDrawingLeadIns).ToList();
        }

        /// <summary>
        /// The operator's notice after Assign Lead-ins kept parts with drawing pins, or null when it
        /// kept none. Part numbers are the editor's (the plate's current order).
        /// </summary>
        public static string KeptMessage(IReadOnlyList<(int PlateNumber, IReadOnlyList<int> PartNumbers)> kept)
        {
            var count = kept.Sum(k => k.PartNumbers.Count);
            if (count == 0)
                return null;
            var where = string.Join("; ", kept.Where(k => k.PartNumbers.Count != 0).Select(k =>
                $"plate {k.PlateNumber}: part{(k.PartNumbers.Count == 1 ? "" : "s")} {string.Join(", ", k.PartNumbers)}"));
            return $"{count} part{(count == 1 ? "" : "s")} kept {(count == 1 ? "its" : "their")} drawing lead-ins ({where}); "
                + "use Plan Cutting to apply their drawing pins.";
        }

        // Locked parts are skipped as before and keep their locked lead-ins whatever their pins.
        private static bool KeepsDrawingLeadIns(Part part) => !part.LeadInsLocked && part.HasActiveDrawingPins;

        private Vector[] AssignPass(
            List<SequencedPart> sequenced,
            CuttingParameters parameters,
            Vector exitPoint,
            Vector[] nextPiercePoints
        )
        {
            var piercePoints = new Vector[sequenced.Count];
            var currentPoint = exitPoint;

            for (var i = 0; i < sequenced.Count; i++)
            {
                var part = sequenced[i].Part;

                if (part.LeadInsLocked || KeepsDrawingLeadIns(part))
                {
                    piercePoints[i] = GetPiercePoint(part);
                    currentPoint = part.Location;
                    continue;
                }

                if (part.HasManualLeadIns)
                    part.RemoveLeadIns();

                var localApproach = currentPoint - part.Location;

                if (nextPiercePoints != null && i + 1 < sequenced.Count)
                {
                    var nextStart = nextPiercePoints[i + 1] - part.Location;
                    part.ApplyLeadIns(parameters, localApproach, nextStart);
                }
                else
                {
                    part.ApplyLeadIns(parameters, localApproach);
                }

                piercePoints[i] = GetPiercePoint(part);
                currentPoint = part.Location;
            }

            return piercePoints;
        }

        private static Vector GetPiercePoint(Part part)
        {
            foreach (var code in part.Program.Codes)
            {
                if (code is CNC.Motion motion)
                    return motion.EndPoint + part.Location;
            }

            return part.Location;
        }
    }
}
