using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
    int? PartNumber, int? OtherPartNumber, string Message);

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

    public string ToDisplayText()
    {
        var text = new StringBuilder();
        text.AppendLine("Pre-post verification");
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
