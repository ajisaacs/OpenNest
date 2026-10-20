using OpenNest.CNC;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;

namespace OpenNest.Tests;

/// <summary>
/// Gives every unlocked part on a plate its lead-ins from the plate's cutting parameters, left
/// to right, the way the retired Assign Lead-ins command did. Post fixtures use it to build
/// programs with lead-ins without running Plan Cutting, so their expected output is unchanged.
/// </summary>
internal static class TestLeadIns
{
    public static void Apply(Plate plate)
    {
        var parameters = plate.CuttingParameters;
        if (parameters == null)
            return;

        var sequenced = new LeftSideSequencer().Sequence(plate.Parts.ToList(), plate);
        var exitPoint = PlateHelper.GetExitPoint(plate);
        // The first pass fixes each part's pierce; the second faces each part toward the next pierce.
        var piercePoints = Pass(null);
        Pass(piercePoints);

        Vector[] Pass(Vector[] nextPiercePoints)
        {
            var pierces = new Vector[sequenced.Count];
            var current = exitPoint;
            for (var i = 0; i < sequenced.Count; i++)
            {
                var part = sequenced[i].Part;
                if (!part.LeadInsLocked)
                {
                    if (part.HasManualLeadIns)
                        part.RemoveLeadIns();
                    var approach = current - part.Location;
                    if (nextPiercePoints != null && i + 1 < sequenced.Count)
                        part.ApplyLeadIns(parameters, approach, nextPiercePoints[i + 1] - part.Location);
                    else
                        part.ApplyLeadIns(parameters, approach);
                }
                pierces[i] = Pierce(part);
                current = part.Location;
            }
            return pierces;
        }
    }

    private static Vector Pierce(Part part)
    {
        foreach (var code in part.Program.Codes)
            if (code is Motion motion)
                return motion.EndPoint + part.Location;
        return part.Location;
    }
}
