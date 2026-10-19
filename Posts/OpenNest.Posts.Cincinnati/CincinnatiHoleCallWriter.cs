using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Posts.Cincinnati;

internal static class CincinnatiHoleCallWriter
{
    /// <param name="origin">
    /// Position of the calling program's frame in the current work coordinate system:
    /// the part location for sheet-level calls, zero inside a normalized part sub-program.
    /// </param>
    /// <param name="holeSubprograms">
    /// Maps each call's hole program (by reference) to its posted sub-program number.
    /// Unmapped calls fall back to the call's own id.
    /// </param>
    /// <param name="partName">Part-name comment to emit when the call opens a part, else null.</param>
    /// <param name="rapidDistance">
    /// Travel from the call to the next pierce, which selects the safety head raise;
    /// null when unknown.
    /// </param>
    internal static void Write(
        TextWriter w,
        SubProgramCall call,
        Vector origin,
        int featureIndex,
        bool isLastFeature,
        CincinnatiPostConfig config,
        CoordinateFormatter fmt,
        IReadOnlyDictionary<Program, int> holeSubprograms,
        string partName = null,
        double? rapidDistance = null
    )
    {
        var postSubNum =
            holeSubprograms != null
            && call.Program != null
            && holeSubprograms.TryGetValue(call.Program, out var num)
                ? num
                : call.Id;

        var featureNumber =
            featureIndex == 0 ? config.FeatureLineNumberStart : 1000 + featureIndex + 1;

        // Shift the local origin to the hole center via G52 (manual §1.52).
        // G52 does not move the nozzle: the hole-local sub-program's first rapid
        // goes directly from the previous feature's end to pierce.
        // See docs/cincinnati-post-output.md for the full bracket.
        var center = origin + call.Offset;
        var sb = new StringBuilder();
        if (config.UseLineNumbers)
            sb.Append($"N{featureNumber} ");
        sb.Append($"G52 X{fmt.FormatCoord(center.X)} Y{fmt.FormatCoord(center.Y)}");
        w.WriteLine(sb.ToString());

        if (!string.IsNullOrEmpty(partName))
            w.WriteLine(CoordinateFormatter.Comment($"PART: {partName}"));

        w.WriteLine($"M98 P{postSubNum}");

        // Cancel the local shift (manual §1.52).
        w.WriteLine("G52 X0 Y0");

        if (isLastFeature)
            return;

        if (CincinnatiHeadRaise.IsSafetyRapid(config, rapidDistance))
            w.WriteLine(CincinnatiHeadRaise.SafetyLine(config));
        else
            w.WriteLine("M47");
    }
}
