using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenNest.Geometry;

namespace OpenNest.Diagnostics;

public enum PostVerificationKind
{
    Overlap,
    MissingLeadIn,
    RapidCrossing,
    Incomplete,

    /// <summary>
    /// A violation inside a fixed emission (a locked program, or a contour cut exactly as its
    /// drawing pins it): accepted as given, never repaired, always reported for review.
    /// </summary>
    FixedLead,

    /// <summary>A part inside another part's cutout is cut after that host, whose cutout releases it.</summary>
    NestedCutOrder,

    /// <summary>
    /// A lead of a nested part or of its host contacts or enters the other's material; for a host
    /// this is typically an enclosing-hole lead crossing the insert.
    /// </summary>
    NestedLead,

    /// <summary>A lead-in pierce is closer to part material than half the pierce diameter.</summary>
    PierceZone,
    /// <summary>
    /// Two parts are closer than the plate's part spacing without sharing material, judged as
    /// Auto Nest's layout check judges them.
    /// </summary>
    PartSpacing,

    /// <summary>
    /// A part its plate's support bed cannot hold level has no tab that keeps it level, so it may
    /// tip once cut free (see docs/support-tabs.md).
    /// </summary>
    PartSupport
}

/// <summary>Plate and part numbers are one-based; plate zero denotes a whole-post limitation.</summary>
public sealed record PostVerificationFinding(PostVerificationKind Kind, int PlateNumber,
    int? PartNumber, int? OtherPartNumber, string Message)
{
    public Vector? Location { get; init; }
    public Vector? RapidStart { get; init; }
    public Vector? RapidEnd { get; init; }
    /// <summary>Rapid boundary contacts in travel order; collinear contact spans use their endpoints.</summary>
    public IReadOnlyList<Vector> ContactPoints { get; init; } = Array.Empty<Vector>();
    public PlateOverlapPair Overlap { get; init; }
    /// <summary>Active clamp contacted by a cut-off or direct XY rapid, if applicable.</summary>
    public string ClampId { get; init; }
}

/// <summary>Owned, immutable findings. Consent is evaluated afresh, never stored.</summary>
public sealed class PostVerificationReport
{
    internal PostVerificationReport(IEnumerable<PostVerificationFinding> findings)
    {
        Findings = Array.AsReadOnly(findings.ToArray());
    }

    public IReadOnlyList<PostVerificationFinding> Findings { get; }
    public bool HasWarnings => Findings.Count != 0;
    public bool CanPost(bool risksAcknowledged) => !HasWarnings || risksAcknowledged;

    public string ToDisplayText() => ToDisplayText("Pre-post verification");

    public string ToDisplayText(string title)
    {
        var text = new StringBuilder();
        text.AppendLine(title);
        var incomplete = Findings.Any(finding => finding.Kind == PostVerificationKind.Incomplete);
        Summary(PostVerificationKind.Overlap, "Overlap");
        Summary(PostVerificationKind.MissingLeadIn, "Missing lead-ins");
        Summary(PostVerificationKind.RapidCrossing, "Rapid crossings");
        // Only nests with fixed-lead findings gain the line, so other reports read as before.
        if (Findings.Any(finding => finding.Kind == PostVerificationKind.FixedLead))
            Summary(PostVerificationKind.FixedLead, "Fixed leads");
        if (Findings.Any(finding => finding.Kind == PostVerificationKind.NestedCutOrder))
            Summary(PostVerificationKind.NestedCutOrder, "Nested part order");
        if (Findings.Any(finding => finding.Kind == PostVerificationKind.NestedLead))
            Summary(PostVerificationKind.NestedLead, "Nested part leads");
        if (Findings.Any(finding => finding.Kind == PostVerificationKind.PierceZone))
            Summary(PostVerificationKind.PierceZone, "Pierce zones");
        if (Findings.Any(finding => finding.Kind == PostVerificationKind.PartSpacing))
            Summary(PostVerificationKind.PartSpacing, "Part spacing");
        if (Findings.Any(finding => finding.Kind == PostVerificationKind.PartSupport))
            Summary(PostVerificationKind.PartSupport, "Part support");
        foreach (var finding in Findings)
        {
            text.Append(finding.PlateNumber == 0 ? "Post processor" : $"Plate {finding.PlateNumber}");
            if (finding.PartNumber is { } part)
                text.Append($", part {part}");
            if (finding.OtherPartNumber is { } other)
                text.Append($", other part {other}");
            text.AppendLine($": {finding.Kind}: {finding.Message}");
        }
        text.AppendLine("These checks cover direct XY rapids in plate/program order; the post may " +
            "change order, routing or retracts. Inspect the posted machine program and machine setup " +
            "before cutting, and confirm actual contour gaps give adequate retention.");
        return text.ToString();

        void Summary(PostVerificationKind kind, string label)
        {
            var count = Findings.Count(finding => finding.Kind == kind);
            text.AppendLine($"{label}: {count} warning(s)" +
                (incomplete ? "; verification incomplete — review before posting." : "."));
        }
    }
}
