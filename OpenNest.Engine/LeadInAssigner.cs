using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;
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
            var currentPoint = PlateHelper.GetExitPoint(plate);

            foreach (var sp in sequenced)
            {
                var part = sp.Part;

                if (part.LeadInsLocked)
                {
                    currentPoint = part.Location;
                    continue;
                }

                if (part.HasManualLeadIns)
                    part.RemoveLeadIns();

                var localApproach = currentPoint - part.Location;
                part.ApplyLeadIns(parameters, localApproach);

                currentPoint = part.Location;
            }
        }
    }
}
