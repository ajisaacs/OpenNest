using System.Collections.Generic;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>Which motions of an execution cut a break line: its lead-in and its break moves.</summary>
internal static class BreakCutMotions
{
    /// <summary>True for every <see cref="LayerType.Break"/> motion and the lead-in run directly before one.</summary>
    internal static bool[] Mask(IReadOnlyList<ExecutionMotion> motions)
    {
        var mask = new bool[motions.Count];
        for (var i = 0; i < motions.Count; i++)
        {
            if (motions[i].Rapid || motions[i].Layer != LayerType.Break)
                continue;
            mask[i] = true;
            for (var lead = i - 1; lead >= 0 && !motions[lead].Rapid && motions[lead].Layer == LayerType.Leadin; lead--)
                mask[lead] = true;
        }
        return mask;
    }
}
