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
    public IReadOnlyList<CutOffClampFinding> DisplayCutOffClampFindings { get; private set; } =
        Array.Empty<CutOffClampFinding>();
    /// <summary>Known part-spacing pairs safe to draw, retained like <see cref="DisplayPairs"/>.</summary>
    public IReadOnlyList<PlateSpacingPair> DisplaySpacingPairs { get; private set; } =
        Array.Empty<PlateSpacingPair>();
    public bool IsRunning => Status == OverlapCheckStatus.Checking;
    /// <summary>A current, fully checked report with no finding of any kind.</summary>
    public bool IsAllClear => Status == OverlapCheckStatus.Current && Report.Pairs.Count == 0
        && Report.ClampFindings.Count == 0 && Report.CutOffClampFindings.Count == 0
        && Report.SpacingPairs.Count == 0 && Report.EdgeFindings.Count == 0;
    /// <summary>
    /// The last published result's message, kept through layout edits and rechecks so the canvas
    /// stays quiet until the next result; never a claim about the current layout. Null when none.
    /// </summary>
    public string SettledMessage { get; private set; }
    /// <summary>Whether that last published result was all clear.</summary>
    public bool SettledClear { get; private set; }

    public string Message => Status switch
    {
        OverlapCheckStatus.Checking => "Checking overlaps…",
        OverlapCheckStatus.Current => CurrentMessage(),
        OverlapCheckStatus.Incomplete => $"Overlap check incomplete: {Report.Pairs.Count} overlapping pairs; "
            + (Report.ClampFindings.Count == 0 ? "" : $"{Report.ClampFindings.Count} clamp findings; ")
            + (Report.CutOffClampFindings.Count == 0 ? ""
                : $"{Report.CutOffClampFindings.Count} cut-off/clamp contacts; ")
            + (Report.SpacingPairs.Count == 0 ? "" : $"{Report.SpacingPairs.Count} pairs under part spacing; ")
            + Edges("; ")
            + $"{uncheckedPartCount} parts could not be checked",
        OverlapCheckStatus.Failed => "Overlap check failed — run Check Overlaps again",
        OverlapCheckStatus.Canceled => "Overlap check canceled",
        OverlapCheckStatus.Stale => "Overlap check out of date — run Check Overlaps again",
        _ => "Overlaps: not checked"
    };

    /// <summary>A few steady words for the status bar; <see cref="Message"/> has the detail.</summary>
    public string Summary => Status switch
    {
        OverlapCheckStatus.Checking => "Layout: checking…",
        OverlapCheckStatus.Current => IsAllClear ? "Layout: OK" : "Layout: " + Findings(),
        OverlapCheckStatus.Incomplete => "Layout: check incomplete",
        OverlapCheckStatus.Failed => "Layout: check failed",
        OverlapCheckStatus.Canceled => "Layout: check canceled",
        OverlapCheckStatus.Stale => "Layout: out of date",
        _ => "Layout: not checked"
    };

    /// <summary>True when <see cref="Summary"/> reports findings or a check that did not finish.</summary>
    public bool NeedsAttention => Status is OverlapCheckStatus.Incomplete or OverlapCheckStatus.Failed
        || Status == OverlapCheckStatus.Current && !IsAllClear;

    private string Findings()
    {
        var overlaps = Report.Pairs.Count + Report.ClampFindings.Count + Report.CutOffClampFindings.Count;
        var spacing = Report.SpacingPairs.Count;
        var words = new List<string>();
        if (overlaps != 0)
            words.Add(overlaps == 1 ? "1 overlap" : $"{overlaps} overlaps");
        if (spacing != 0)
            words.Add($"{spacing} too close");
        var off = Report.EdgeFindings.Count(finding => finding.OffPlate);
        var near = Report.EdgeFindings.Count - off;
        if (off != 0)
            words.Add($"{off} off plate");
        if (near != 0)
            words.Add($"{near} near edge");
        return string.Join(", ", words);
    }

    // Plate-edge counts for the detailed message, each followed by the separator; empty when none.
    private string Edges(string separator)
    {
        var off = Report.EdgeFindings.Count(finding => finding.OffPlate);
        var near = Report.EdgeFindings.Count - off;
        return (off == 0 ? "" : $"{off} parts off the plate{separator}")
            + (near == 0 ? "" : $"{near} parts inside the edge spacing{separator}");
    }

    // Only a fully checked report that required a gap may clear spacing as well as overlaps.
    private string CurrentMessage()
    {
        var spacing = (Report.SpacingPairs.Count == 0 ? ""
            : $"; part spacing: {Report.SpacingPairs.Count} pairs too close")
            + (Report.EdgeFindings.Count == 0 ? "" : "; " + Edges("; ").TrimEnd(' ', ';'));
        if (Report.ClampFindings.Count != 0 || Report.CutOffClampFindings.Count != 0)
            return $"Overlaps: {Report.Pairs.Count} pairs, {Report.ClampFindings.Count} clamps, "
                + $"{Report.CutOffClampFindings.Count} cut-off/clamp contacts{spacing}";
        if (Report.Pairs.Count != 0)
            return $"Overlaps: {Report.Pairs.Count} pairs{spacing}";
        if (spacing.Length != 0)
            return "No material overlaps" + spacing;
        return Report.SpacingChecked ? "No material overlaps or part spacing violations detected"
            : "No material overlaps detected";
    }

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
        DisplayPairs = report.ClampFindings.Count == 0 ? report.Pairs
            : report.Pairs.Concat(report.ClampFindings).ToList().AsReadOnly();
        DisplayCutOffClampFindings = report.CutOffClampFindings;
        DisplaySpacingPairs = report.SpacingPairs;
        displayStamp = stamp;
        observedDisplayStamp = stamp;
        uncheckedPartCount = CountUncheckedParts(report.Issues.Concat(report.SpacingIssues));
        Status = report.IsComplete ? OverlapCheckStatus.Current : OverlapCheckStatus.Incomplete;
        SettledMessage = Message;
        SettledClear = IsAllClear;
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
        var sameZones = displayStamp.SameZones(plate);
        if (!sameZones || !displayStamp.SameCutOffs(plate))
            DisplayCutOffClampFindings = Array.Empty<CutOffClampFinding>();
        var retained = DisplayPairs.Where(pair => unchanged[pair.PartAId]
            && (pair.PartBId < 0 ? sameZones : unchanged[pair.PartBId])).ToList();
        if (retained.Count != DisplayPairs.Count)
            DisplayPairs = retained.AsReadOnly();
        var sameSpacing = displayStamp.SameSpacing(plate);
        var retainedSpacing = DisplaySpacingPairs.Where(pair => sameSpacing
            && unchanged[pair.PartAId] && unchanged[pair.PartBId]).ToList();
        if (retainedSpacing.Count != DisplaySpacingPairs.Count)
            DisplaySpacingPairs = retainedSpacing.AsReadOnly();
        if (DisplayPairs.Count == 0 && DisplayCutOffClampFindings.Count == 0 && DisplaySpacingPairs.Count == 0)
            ClearDisplayPairs();
        else
            observedDisplayStamp = OverlapGeometryStamp.Capture(plate);
    }

    private void ClearDisplayPairs()
    {
        DisplayPairs = Array.Empty<PlateOverlapPair>();
        DisplayCutOffClampFindings = Array.Empty<CutOffClampFinding>();
        DisplaySpacingPairs = Array.Empty<PlateSpacingPair>();
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
        {
            ClearDisplayPairs();
            SettledMessage = null;
            SettledClear = false;
        }
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
