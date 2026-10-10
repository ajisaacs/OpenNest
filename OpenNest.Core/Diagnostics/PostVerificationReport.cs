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
    Incomplete
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
        foreach (var finding in Findings)
        {
            text.Append(finding.PlateNumber == 0 ? "Post processor" : $"Plate {finding.PlateNumber}");
            if (finding.PartNumber is { } part)
                text.Append($", part {part}");
            if (finding.OtherPartNumber is { } other)
                text.Append($", other part {other}");
            text.AppendLine($": {finding.Kind}: {finding.Message}");
        }
        text.AppendLine("This is not a physical safety certification. The check uses direct XY rapids " +
            "in plate/program order; the post may change order, routing or retracts. Inspect the posted " +
            "machine program and machine setup. Actual contour gaps are not proof of adequate retention.");
        return text.ToString();

        void Summary(PostVerificationKind kind, string label)
        {
            var count = Findings.Count(finding => finding.Kind == kind);
            text.AppendLine($"{label}: {count} warning(s)" +
                (incomplete ? "; verification incomplete — do not treat as clear." : "."));
        }
    }
}
