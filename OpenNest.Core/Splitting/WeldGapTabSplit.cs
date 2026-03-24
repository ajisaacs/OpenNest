using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest;

/// <summary>
/// Generates rectangular tabs on one side of the split edge (negative side).
/// The positive side remains a straight line. Tabs act as weld-gap spacers.
/// </summary>
public class WeldGapTabSplit : ISplitFeature
{
    public string Name => "Weld-Gap Tabs";

    public SplitFeatureResult GenerateFeatures(SplitLine line, double extentStart, double extentEnd, SplitParameters parameters)
    {
        var extent = extentEnd - extentStart;
        var tabCount = parameters.TabCount;
        var tabWidth = parameters.TabWidth;
        var tabHeight = parameters.TabHeight;

        // Evenly space tabs along the split line
        var spacing = extent / (tabCount + 1);

        var negEntities = new List<Entity>();
        var isVertical = line.Axis == CutOffAxis.Vertical;
        var pos = line.Position;

        // Tabs protrude toward the negative side (lower coordinate on the split axis)
        var tabDir = -1.0;

        var cursor = extentStart;

        for (var i = 0; i < tabCount; i++)
        {
            var tabCenter = extentStart + spacing * (i + 1);
            var tabStart = tabCenter - tabWidth / 2;
            var tabEnd = tabCenter + tabWidth / 2;

            if (isVertical)
            {
                if (tabStart > cursor + OpenNest.Math.Tolerance.Epsilon)
                    negEntities.Add(new Line(new Vector(pos, cursor), new Vector(pos, tabStart)));

                negEntities.Add(new Line(new Vector(pos, tabStart), new Vector(pos + tabDir * tabHeight, tabStart)));
                negEntities.Add(new Line(new Vector(pos + tabDir * tabHeight, tabStart), new Vector(pos + tabDir * tabHeight, tabEnd)));
                negEntities.Add(new Line(new Vector(pos + tabDir * tabHeight, tabEnd), new Vector(pos, tabEnd)));
            }
            else
            {
                if (tabStart > cursor + OpenNest.Math.Tolerance.Epsilon)
                    negEntities.Add(new Line(new Vector(cursor, pos), new Vector(tabStart, pos)));

                negEntities.Add(new Line(new Vector(tabStart, pos), new Vector(tabStart, pos + tabDir * tabHeight)));
                negEntities.Add(new Line(new Vector(tabStart, pos + tabDir * tabHeight), new Vector(tabEnd, pos + tabDir * tabHeight)));
                negEntities.Add(new Line(new Vector(tabEnd, pos + tabDir * tabHeight), new Vector(tabEnd, pos)));
            }

            cursor = tabEnd;
        }

        // Final segment from last tab to extent end
        if (isVertical)
        {
            if (extentEnd > cursor + OpenNest.Math.Tolerance.Epsilon)
                negEntities.Add(new Line(new Vector(pos, cursor), new Vector(pos, extentEnd)));
        }
        else
        {
            if (extentEnd > cursor + OpenNest.Math.Tolerance.Epsilon)
                negEntities.Add(new Line(new Vector(cursor, pos), new Vector(extentEnd, pos)));
        }

        // Positive side: plain straight line (reversed direction)
        var posEntities = new List<Entity>();
        if (isVertical)
            posEntities.Add(new Line(new Vector(pos, extentEnd), new Vector(pos, extentStart)));
        else
            posEntities.Add(new Line(new Vector(extentEnd, pos), new Vector(extentStart, pos)));

        return new SplitFeatureResult(negEntities, posEntities);
    }
}
