using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Engine
{
    public class LeadInAssigner
    {
        public IPartSequencer Sequencer { get; set; }

        public void Assign(Plate plate)
        {
            var parameters = plate.CuttingParameters;
            if (parameters == null)
                return;

            var sequenced = Sequencer.Sequence(plate.Parts.ToList(), plate);
            var exitPoint = PlateHelper.GetExitPoint(plate);

            // Pass 1: assign lead-ins to establish pierce points
            var piercePoints = AssignPass(sequenced, parameters, exitPoint, nextPiercePoints: null);

            // Pass 2: re-assign with knowledge of next part's start point
            AssignPass(sequenced, parameters, exitPoint, nextPiercePoints: piercePoints);
        }

        private Vector[] AssignPass(List<SequencedPart> sequenced, CuttingParameters parameters,
            Vector exitPoint, Vector[] nextPiercePoints)
        {
            var piercePoints = new Vector[sequenced.Count];
            var currentPoint = exitPoint;

            for (var i = 0; i < sequenced.Count; i++)
            {
                var part = sequenced[i].Part;

                if (part.LeadInsLocked)
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
