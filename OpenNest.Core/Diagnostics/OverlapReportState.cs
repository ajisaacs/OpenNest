using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Diagnostics;

public enum OverlapDisplayMode { Off, Areas, Centroids, Both }
public enum OverlapCheckStatus { NotChecked, Checking, Current, Incomplete, Failed, Canceled, Stale }

/// <summary>UI-thread lifecycle policy, independent of workers, GDI and view transforms.</summary>
public sealed class OverlapReportState
{
    private OverlapGeometryStamp stamp;
    private OverlapGeometryStamp displayStamp;
    private OverlapGeometryStamp observedDisplayStamp;
    private int uncheckedPartCount;

    public long Generation { get; private set; }
    public OverlapCheckStatus Status { get; private set; } = OverlapCheckStatus.NotChecked;
    public OverlapDisplayMode DisplayMode { get; set; } = OverlapDisplayMode.Areas;
    public PlateOverlapReport Report { get; private set; }
    /// <summary>Known overlap pairs safe to draw, even while the full layout needs a recheck.</summary>
    public IReadOnlyList<PlateOverlapPair> DisplayPairs { get; private set; } = Array.Empty<PlateOverlapPair>();
    public bool IsRunning => Status == OverlapCheckStatus.Checking;

    public string Message => Status switch
    {
        OverlapCheckStatus.Checking => "Checking overlaps…",
        OverlapCheckStatus.Current => Report.Pairs.Count == 0
            ? "No material overlaps detected" : $"Overlaps: {Report.Pairs.Count} pairs",
        OverlapCheckStatus.Incomplete => $"Overlap check incomplete: {Report.Pairs.Count} overlapping pairs; "
            + $"{uncheckedPartCount} parts could not be checked",
        OverlapCheckStatus.Failed => "Overlap check failed — run Check Overlaps again",
        OverlapCheckStatus.Canceled => "Overlap check canceled",
        OverlapCheckStatus.Stale => "Overlap check out of date — run Check Overlaps again",
        _ => "Overlaps: not checked"
    };

    /// <summary>
    /// Starts a request. A manual check from Off shows Areas; an automatic recheck keeps
    /// the user's display choice, including Off.
    /// </summary>
    public long Begin(Plate plate, bool automatic = false)
    {
        RefreshDisplayPairs(plate);
        Clear(OverlapCheckStatus.Checking, preserveDisplay: true);
        stamp = OverlapGeometryStamp.Capture(plate);
        if (!automatic && DisplayMode == OverlapDisplayMode.Off)
            DisplayMode = OverlapDisplayMode.Areas;
        return Generation;
    }

    public bool TryPublish(long generation, Plate plate, PlateOverlapReport report)
    {
        if (!CanComplete(generation, plate))
            return false;
        Report = report;
        DisplayPairs = report.Pairs;
        displayStamp = stamp;
        observedDisplayStamp = stamp;
        uncheckedPartCount = CountUncheckedParts(report.Issues);
        Status = report.IsComplete ? OverlapCheckStatus.Current : OverlapCheckStatus.Incomplete;
        return true;
    }

    public bool TryFail(long generation, Plate plate)
    {
        if (!CanComplete(generation, plate))
            return false;
        Clear(OverlapCheckStatus.Failed);
        return true;
    }

    private bool CanComplete(long generation, Plate plate) =>
        generation == Generation && IsRunning && EnsureFresh(plate);

    public bool EnsureFresh(Plate plate)
    {
        RefreshDisplayPairs(plate);
        if (stamp == null)
            return false;
        if (stamp.Matches(plate))
            return true;
        Invalidate(plate);
        return false;
    }

    /// <summary>Layout edit: retain only pairs whose two ordered slots still match exactly.</summary>
    public void Invalidate(Plate plate)
    {
        RefreshDisplayPairs(plate);
        if (Status is OverlapCheckStatus.Checking or OverlapCheckStatus.Current or OverlapCheckStatus.Incomplete)
            Clear(OverlapCheckStatus.Stale, preserveDisplay: true);
    }

    /// <summary>In-place geometry edits and teardown must forget every cached display pair.</summary>
    public void Invalidate()
    {
        ClearDisplayPairs();
        if (Status is OverlapCheckStatus.Checking or OverlapCheckStatus.Current or OverlapCheckStatus.Incomplete)
            Clear(OverlapCheckStatus.Stale);
    }

    private void RefreshDisplayPairs(Plate plate)
    {
        if (displayStamp == null || observedDisplayStamp?.Matches(plate) == true)
            return;
        var unchanged = displayStamp.UnchangedSlots(plate);
        var retained = DisplayPairs.Where(pair => unchanged[pair.PartAId] && unchanged[pair.PartBId]).ToList();
        if (retained.Count != DisplayPairs.Count)
            DisplayPairs = retained.AsReadOnly();
        if (DisplayPairs.Count == 0)
            ClearDisplayPairs();
        else
            observedDisplayStamp = OverlapGeometryStamp.Capture(plate);
    }

    private void ClearDisplayPairs()
    {
        DisplayPairs = Array.Empty<PlateOverlapPair>();
        displayStamp = null;
        observedDisplayStamp = null;
    }

    public void Cancel()
    {
        if (IsRunning)
            Clear(OverlapCheckStatus.Canceled);
    }

    public void Reset() => Clear(OverlapCheckStatus.NotChecked);

    private void Clear(OverlapCheckStatus status, bool preserveDisplay = false)
    {
        if (!preserveDisplay)
            ClearDisplayPairs();
        Generation++;
        Report = null;
        uncheckedPartCount = 0;
        stamp = null;
        Status = status;
    }

    public static int CountUncheckedParts(IEnumerable<PlateOverlapIssue> issues)
    {
        var ids = new HashSet<int>();
        foreach (var issue in issues)
        {
            ids.Add(issue.PartAId);
            if (issue.PartBId.HasValue)
                ids.Add(issue.PartBId.Value);
        }
        return ids.Count;
    }
}
