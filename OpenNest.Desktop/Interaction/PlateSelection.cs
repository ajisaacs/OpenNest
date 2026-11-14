using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.CutOffs;

namespace OpenNest.Desktop.Interaction;

/// <summary>
/// The parts and cut-offs selected on one plate view. Selecting a common-line member brings its
/// whole group, so the joined parts move, rotate and push as one unit and keep their shared spans
/// (see docs/common-line-cutting.md). Cut-off segments are never selected as parts; cut-offs are
/// selected as their definitions. <see cref="Changed"/> is raised once per call that changes it.
/// </summary>
public sealed class PlateSelection
{
    private readonly List<Part> parts = new();
    private readonly HashSet<Part> partSet = new(ReferenceEqualityComparer.Instance);
    private readonly List<CutOff> cutOffs = new();
    private Plate plate;

    /// <summary>The plate selected on; changing it clears the selection.</summary>
    public Plate Plate
    {
        get => plate;
        set
        {
            if (ReferenceEquals(plate, value))
                return;
            plate = value;
            Clear();
        }
    }

    /// <summary>The selected parts, in the order they were selected.</summary>
    public IReadOnlyList<Part> Parts => parts;

    /// <summary>The selected cut-off definitions.</summary>
    public IReadOnlyList<CutOff> CutOffs => cutOffs;

    public bool IsEmpty => parts.Count == 0 && cutOffs.Count == 0;

    public event EventHandler Changed;

    public bool Contains(Part part) => part != null && partSet.Contains(part);

    public bool Contains(CutOff cutOff) => cutOffs.Any(c => ReferenceEquals(c, cutOff));

    /// <summary>Deselects every part and cut-off.</summary>
    public void Clear() => Change(() => ClearPartsCore() | ClearCutOffsCore());

    public void ClearParts() => Change(ClearPartsCore);

    public void ClearCutOffs() => Change(ClearCutOffsCore);

    /// <summary>Adds <paramref name="part"/> and the rest of its common-line group.</summary>
    public void Select(Part part) => Change(() => AddCore(part));

    /// <summary>Adds each of <paramref name="toSelect"/> with the rest of its common-line group.</summary>
    public void Select(IEnumerable<Part> toSelect) =>
        Change(() => toSelect.Aggregate(false, (changed, part) => AddCore(part) | changed));

    /// <summary>Replaces the selection with <paramref name="part"/> and its common-line group.</summary>
    public void SelectOnly(Part part) => Change(() => ClearPartsCore() | ClearCutOffsCore() | AddCore(part));

    /// <summary>Adds the cut-off definition <paramref name="cutOff"/>.</summary>
    public void Select(CutOff cutOff) => Change(() => AddCore(cutOff));

    /// <summary>Adds each of <paramref name="toSelect"/>.</summary>
    public void Select(IEnumerable<CutOff> toSelect) =>
        Change(() => toSelect.Aggregate(false, (changed, cutOff) => AddCore(cutOff) | changed));

    /// <summary>Replaces the selection with the cut-off definition <paramref name="cutOff"/>.</summary>
    public void SelectOnly(CutOff cutOff) => Change(() => ClearPartsCore() | ClearCutOffsCore() | AddCore(cutOff));

    /// <summary>Selects every part on the plate (cut-off segments excepted), keeping what is selected.</summary>
    public void SelectAll()
    {
        if (plate != null)
            Select(plate.Parts.ToList());
    }

    /// <summary>
    /// The selected parts as indices into <see cref="Plate"/>'s parts, in plate order; parts no
    /// longer on the plate are left out. With <see cref="SelectIndices"/>, keeps a selection across
    /// an edit that replaces the plate's parts (an undo, say).
    /// </summary>
    public IReadOnlyList<int> PartIndices
    {
        get
        {
            var indices = new List<int>();
            if (plate == null || parts.Count == 0)
                return indices;
            for (var i = 0; i < plate.Parts.Count; i++)
                if (partSet.Contains(plate.Parts[i]))
                    indices.Add(i);
            return indices;
        }
    }

    /// <summary>
    /// Replaces the selection with the parts at <paramref name="indices"/> in <see cref="Plate"/>'s
    /// parts (each with its common-line group); indices off the plate are skipped.
    /// </summary>
    public void SelectIndices(IEnumerable<int> indices) =>
        Change(() =>
        {
            var changed = ClearPartsCore() | ClearCutOffsCore();
            if (plate == null || indices == null)
                return changed;
            foreach (var i in indices)
                if (i >= 0 && i < plate.Parts.Count)
                    changed |= AddCore(plate.Parts[i]);
            return changed;
        });

    /// <summary>
    /// Drops parts and cut-offs that are no longer on the plate, as after an edit that replaced
    /// them; true when any were dropped.
    /// </summary>
    public bool Prune()
    {
        if (IsEmpty)
            return false;
        var onPlate = plate == null ? new HashSet<Part>() : new HashSet<Part>(plate.Parts);
        partSet.RemoveWhere(part => !onPlate.Contains(part));
        var removed = parts.RemoveAll(part => !onPlate.Contains(part))
            + cutOffs.RemoveAll(cutOff => plate?.CutOffs.Contains(cutOff) != true);
        if (removed > 0)
            Changed?.Invoke(this, EventArgs.Empty);
        return removed > 0;
    }

    /// <summary>
    /// <paramref name="part"/> followed by the other members of its common-line group on
    /// <paramref name="plate"/>, in plate order.
    /// </summary>
    public static IEnumerable<Part> WithCommonLineGroup(Plate plate, Part part)
    {
        yield return part;
        var group = plate?.CommonLineGroupOf(part);
        if (group == null)
            yield break;
        foreach (var other in plate.Parts)
        {
            if (!ReferenceEquals(other, part) && group.Contains(other))
                yield return other;
        }
    }

    private bool AddCore(Part part)
    {
        if (!PlateHitTester.IsSelectable(part))
            return false;
        var changed = false;
        foreach (var member in WithCommonLineGroup(plate, part))
        {
            if (!partSet.Add(member))
                continue;
            parts.Add(member);
            changed = true;
        }
        return changed;
    }

    private bool AddCore(CutOff cutOff)
    {
        if (cutOff == null || Contains(cutOff))
            return false;
        cutOffs.Add(cutOff);
        return true;
    }

    private bool ClearPartsCore()
    {
        if (parts.Count == 0)
            return false;
        parts.Clear();
        partSet.Clear();
        return true;
    }

    private bool ClearCutOffsCore()
    {
        if (cutOffs.Count == 0)
            return false;
        cutOffs.Clear();
        return true;
    }

    private void Change(Func<bool> change)
    {
        if (change())
            Changed?.Invoke(this, EventArgs.Empty);
    }
}
