using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest;

/// <summary>Existing cutting work on a set of plates: applied lead-ins and cut-offs.</summary>
/// <param name="LeadInParts">Real parts carrying applied lead-ins (locked ones included).</param>
/// <param name="LockedLeadInParts">Of those, parts whose lead-ins are locked.</param>
/// <param name="CutOffs">Cut-off definitions.</param>
/// <param name="PlateNumbers">Display numbers of the plates holding any of it, ascending.</param>
public sealed record AppliedCuttingWorkSummary(int LeadInParts, int LockedLeadInParts, int CutOffs,
    IReadOnlyList<int> PlateNumbers)
{
    public bool HasExistingWork => LeadInParts > 0 || CutOffs > 0;

    /// <summary>Operator text for the cutting planner's warning banner; empty when nothing exists.</summary>
    public string Describe()
    {
        if (!HasExistingWork)
            return string.Empty;
        var found = new List<string>();
        if (LeadInParts > 0)
            found.Add($"lead-ins on {Count(LeadInParts, "part")}"
                + (LockedLeadInParts > 0 ? $" ({LockedLeadInParts} locked)" : string.Empty));
        if (CutOffs > 0)
            found.Add(Count(CutOffs, "cut-off"));
        var plates = PlateNumbers.Count == 1 ? "plate " : "plates ";
        var kept = new List<string>();
        if (LockedLeadInParts > 0)
            kept.Add("locked lead-ins");
        if (CutOffs > 0)
            kept.Add("existing cut-offs");
        return $"ALREADY APPLIED: {string.Join(" and ", found)} on {plates}{string.Join(", ", PlateNumbers)}."
            + (LeadInParts > LockedLeadInParts ? " Planning recalculates unlocked lead-ins." : string.Empty)
            + (kept.Count > 0 ? $" Planning keeps {string.Join(" and ", kept)} unchanged." : string.Empty)
            + " Clear them to recalculate everything from scratch.";
    }

    private static string Count(int value, string noun) => $"{value} {noun}{(value == 1 ? string.Empty : "s")}";
}

/// <summary>Finds and clears applied lead-ins and cut-offs so a plan can recalculate them.</summary>
public static class AppliedCuttingWork
{
    /// <summary>
    /// Counts applied lead-ins on real parts and cut-off definitions. Definitions in
    /// <paramref name="ignoredCutOffs"/> (for example ones staged by the caller) are not counted.
    /// </summary>
    public static AppliedCuttingWorkSummary Summarize(IReadOnlyList<Plate> plates,
        IReadOnlyList<int> plateNumbers = null, IEnumerable<CutOff> ignoredCutOffs = null)
    {
        ArgumentNullException.ThrowIfNull(plates);
        if (plateNumbers != null && plateNumbers.Count != plates.Count)
            throw new ArgumentException("Give one display number per plate.", nameof(plateNumbers));
        var ignored = new HashSet<CutOff>(ignoredCutOffs ?? [], ReferenceEqualityComparer.Instance);
        int leads = 0, locked = 0, cutOffs = 0;
        var numbers = new SortedSet<int>();
        for (var index = 0; index < plates.Count; index++)
        {
            var plate = plates[index] ?? throw new ArgumentException("Plates must not be null.", nameof(plates));
            var plateLeads = plate.Parts.Where(p => !p.BaseDrawing.IsCutOff && p.HasManualLeadIns).ToArray();
            var plateCutOffs = plate.CutOffs.Count(c => !ignored.Contains(c));
            leads += plateLeads.Length;
            locked += plateLeads.Count(p => p.LeadInsLocked);
            cutOffs += plateCutOffs;
            if (plateLeads.Length > 0 || plateCutOffs > 0)
                numbers.Add(plateNumbers?[index] ?? index + 1);
        }
        return new(leads, locked, cutOffs, numbers.ToArray());
    }

    /// <summary>
    /// Removes every applied lead-in (locked ones too) from real parts and every cut-off with
    /// its cut-off parts, and forgets each plate's recorded cutting parameters. Part poses,
    /// order and quantities are unchanged. Returns what was cleared.
    /// </summary>
    public static AppliedCuttingWorkSummary Clear(IReadOnlyList<Plate> plates, IReadOnlyList<int> plateNumbers = null)
    {
        var cleared = Summarize(plates, plateNumbers);
        foreach (var plate in plates)
        {
            foreach (var part in plate.Parts.Where(p => !p.BaseDrawing.IsCutOff && p.HasManualLeadIns).ToArray())
                part.RemoveLeadIns();
            plate.CutOffs.Clear();
            for (var index = plate.Parts.Count - 1; index >= 0; index--)
                if (plate.Parts[index].BaseDrawing.IsCutOff)
                    plate.Parts.RemoveAt(index);
            plate.CuttingParameters = null;
        }
        return cleared;
    }
}
