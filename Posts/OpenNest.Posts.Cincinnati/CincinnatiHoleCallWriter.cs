using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenNest.CNC;

namespace OpenNest.Posts.Cincinnati;

internal static class CincinnatiHoleCallWriter
{
    internal static void Write(
        TextWriter w,
        SubProgramCall call,
        int featureIndex,
        bool isLastFeature,
        CincinnatiPostConfig config,
        CoordinateFormatter fmt,
        Dictionary<int, int> holeSubprograms
    )
    {
        var postSubNum =
            holeSubprograms != null && holeSubprograms.TryGetValue(call.Id, out var num)
                ? num
                : call.Id;

        var featureNumber =
            featureIndex == 0 ? config.FeatureLineNumberStart : 1000 + featureIndex + 1;

        // Shift the local origin to the hole center via G52 (manual §1.52).
        // G52 does not move the nozzle: the hole-local sub-program's first rapid
        // goes directly from the previous feature's end to pierce.
        // See docs/cincinnati-post-output.md for the full bracket.
        var sb = new StringBuilder();
        if (config.UseLineNumbers)
            sb.Append($"N{featureNumber} ");
        sb.Append($"G52 X{fmt.FormatCoord(call.Offset.X)} Y{fmt.FormatCoord(call.Offset.Y)}");
        w.WriteLine(sb.ToString());

        w.WriteLine($"M98 P{postSubNum}");

        // Cancel the local shift (manual §1.52).
        w.WriteLine("G52 X0 Y0");

        if (!isLastFeature)
            w.WriteLine("M47");
    }
}
