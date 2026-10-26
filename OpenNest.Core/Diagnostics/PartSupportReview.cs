using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.Support;

namespace OpenNest.Diagnostics;

/// <summary>
/// Pre-post review of part support (docs/support-tabs.md). Every part whose cutting parameters
/// ask for support tabs and that needs one (<see cref="SupportTabSettings.Analyze"/>: its plate's
/// support bed cannot hold it level or, without a bed, its width is in the no-bed range) must be
/// cut with a tab that keeps it level, however its program was made (planned, placed by hand or
/// locked); without a bed any tab will do. The tab is the gap the outside cut leaves between its end and its start. Reports
/// only; a part whose material or program cannot be read is reported as not checked.
/// </summary>
internal static class PartSupportReview
{
    internal static void Add(Plate plate, int plateNumber, List<PostVerificationFinding> findings,
        CancellationToken token)
    {
        var bed = plate.SupportBed;
        for (var index = 0; index < plate.Parts.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var part = plate.Parts[index];
            var parameters = part?.CuttingParameters ?? plate.CuttingParameters;
            if (part?.BaseDrawing == null || part.BaseDrawing.IsCutOff || parameters?.SupportTabs != true
                || (bed == null && !parameters.HasNoBedTabRange()))
                continue;
            var partNumber = index + 1;
            try
            {
                var support = SupportTabSettings.Analyze(parameters, bed, plate.Quadrant, SupportMaterial.FromPart(part));
                if (support == null)
                    continue;
                if (!support.IsComplete)
                {
                    findings.Add(new(PostVerificationKind.Incomplete, plateNumber, partNumber, null,
                        "Support check: " + support.Reason));
                    continue;
                }
                if (support.IsSupported)
                    continue;
                var tab = OutsideTab(ExecutionMotionReader.Read(part.Program, part.Location, null, token));
                if (tab is { } point && support.IsSteadiedByTabAt(point))
                    continue;
                findings.Add(new(PostVerificationKind.PartSupport, plateNumber, partNumber, null,
                    Message(support, tab))
                { Location = support.CenterOfGravity });
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                or NotSupportedException or ArithmeticException)
            {
                findings.Add(new(PostVerificationKind.Incomplete, plateNumber, partNumber, null,
                    "Support check: " + exception.Message));
            }
        }
    }

    /// <summary>
    /// The middle of the gap the outside cut leaves open, or null when it closes: the outside cut
    /// is the chain of cutting motions with the largest extent.
    /// </summary>
    internal static Vector? OutsideTab(OwnedExecution execution)
    {
        Vector? start = null, end = null, bestStart = null, bestEnd = null;
        var minX = double.PositiveInfinity; var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity; var maxY = double.NegativeInfinity;
        var bestArea = -1.0;
        foreach (var motion in execution.Motions.Append(null))
        {
            if (motion is { Rapid: false, Layer: LayerType.Cut or LayerType.Display })
            {
                start ??= motion.Start ?? motion.End;
                end = motion.End;
                foreach (var p in new[] { motion.Start ?? motion.End, motion.End, motion.Curve?.Midpoint ?? motion.End })
                {
                    minX = System.Math.Min(minX, p.X); minY = System.Math.Min(minY, p.Y);
                    maxX = System.Math.Max(maxX, p.X); maxY = System.Math.Max(maxY, p.Y);
                }
                continue;
            }
            // Anything else ends a chain: a rapid, a lead, a scribe or a break cut.
            if (start != null && (maxX - minX) * (maxY - minY) > bestArea)
            {
                bestArea = (maxX - minX) * (maxY - minY);
                bestStart = start;
                bestEnd = end;
            }
            start = end = null;
            minX = minY = double.PositiveInfinity;
            maxX = maxY = double.NegativeInfinity;
        }
        if (bestStart is not { } first || bestEnd is not { } last
            || first.DistanceTo(last) <= PostVerificationGeometry.Epsilon)
            return null;
        return (first + last) * 0.5;
    }

    private static string Message(PartSupport support, Vector? tab)
    {
        // Without a bed any tab steadies the part, so the only failure is no tab at all.
        if (!support.HasBed)
            return FormattableString.Invariant(
                    $"The part is {support.Width:0.####} wide, in the width range tabbed without a support bed, ")
                + "and it has no tab; it may tip once cut free."
                + (support.BestTabPoint is { } suggested
                    ? FormattableString.Invariant($" Tab it near ({suggested.X:0.####}, {suggested.Y:0.####}).")
                    : "");
        var held = support.Margin == double.NegativeInfinity
            ? "rests on no support contact"
            : support.Margin < 0
                ? FormattableString.Invariant($"has its center of gravity {-support.Margin:0.####} outside its support contacts")
                : FormattableString.Invariant($"has its center of gravity only {support.Margin:0.####} inside its support contacts");
        var tabText = tab is { } point
            ? FormattableString.Invariant($"its tab at ({point.X:0.####}, {point.Y:0.####}) does not steady it")
            : "it has no tab";
        var best = support.BestTabPoint is { } bestPoint && support.TabCanSupport
            ? FormattableString.Invariant($" A tab near ({bestPoint.X:0.####}, {bestPoint.Y:0.####}) would.")
            : " No single tab can steady it.";
        return FormattableString.Invariant($"The part {held} (needs {support.RequiredMargin:0.####}) and {tabText}; ")
            + "it may tip once cut free." + best;
    }
}
