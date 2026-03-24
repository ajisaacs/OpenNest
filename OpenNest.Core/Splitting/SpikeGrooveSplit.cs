using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest;

/// <summary>
/// Generates interlocking spike/V-groove pairs along the split edge.
/// Spikes protrude from the positive side into the negative side.
/// V-grooves on the negative side receive the spikes for self-alignment during welding.
/// </summary>
public class SpikeGrooveSplit : ISplitFeature
{
    public string Name => "Spike / V-Groove";

    public SplitFeatureResult GenerateFeatures(SplitLine line, double extentStart, double extentEnd, SplitParameters parameters)
    {
        var extent = extentEnd - extentStart;
        var pairCount = parameters.SpikePairCount;
        var depth = parameters.SpikeDepth;
        var angleRad = OpenNest.Math.Angle.ToRadians(parameters.SpikeAngle / 2);
        var halfWidth = depth * System.Math.Tan(angleRad);

        var isVertical = line.Axis == CutOffAxis.Vertical;
        var pos = line.Position;

        // Place pairs evenly: one near each end, with margin
        var margin = extent * 0.15;
        var pairPositions = new List<double>();
        if (pairCount == 1)
        {
            pairPositions.Add(extentStart + extent / 2);
        }
        else
        {
            var usable = extent - 2 * margin;
            for (var i = 0; i < pairCount; i++)
                pairPositions.Add(extentStart + margin + usable * i / (pairCount - 1));
        }

        var negEntities = BuildGrooveSide(pairPositions, halfWidth, depth, extentStart, extentEnd, pos, isVertical);
        var posEntities = BuildSpikeSide(pairPositions, halfWidth, depth, extentStart, extentEnd, pos, isVertical);

        return new SplitFeatureResult(negEntities, posEntities);
    }

    private static List<Entity> BuildGrooveSide(List<double> pairPositions, double halfWidth, double depth,
        double extentStart, double extentEnd, double pos, bool isVertical)
    {
        var entities = new List<Entity>();
        var cursor = extentStart;

        foreach (var center in pairPositions)
        {
            var grooveStart = center - halfWidth;
            var grooveEnd = center + halfWidth;

            if (grooveStart > cursor + OpenNest.Math.Tolerance.Epsilon)
                entities.Add(MakeLine(pos, cursor, pos, grooveStart, isVertical));

            entities.Add(MakeLine(pos, grooveStart, pos - depth, center, isVertical));
            entities.Add(MakeLine(pos - depth, center, pos, grooveEnd, isVertical));

            cursor = grooveEnd;
        }

        if (extentEnd > cursor + OpenNest.Math.Tolerance.Epsilon)
            entities.Add(MakeLine(pos, cursor, pos, extentEnd, isVertical));

        return entities;
    }

    private static List<Entity> BuildSpikeSide(List<double> pairPositions, double halfWidth, double depth,
        double extentStart, double extentEnd, double pos, bool isVertical)
    {
        var entities = new List<Entity>();
        var cursor = extentEnd;

        for (var i = pairPositions.Count - 1; i >= 0; i--)
        {
            var center = pairPositions[i];
            var spikeEnd = center + halfWidth;
            var spikeStart = center - halfWidth;

            if (cursor > spikeEnd + OpenNest.Math.Tolerance.Epsilon)
                entities.Add(MakeLine(pos, cursor, pos, spikeEnd, isVertical));

            entities.Add(MakeLine(pos, spikeEnd, pos - depth, center, isVertical));
            entities.Add(MakeLine(pos - depth, center, pos, spikeStart, isVertical));

            cursor = spikeStart;
        }

        if (cursor > extentStart + OpenNest.Math.Tolerance.Epsilon)
            entities.Add(MakeLine(pos, cursor, pos, extentStart, isVertical));

        return entities;
    }

    private static Line MakeLine(double splitAxis1, double along1, double splitAxis2, double along2, bool isVertical)
    {
        return isVertical
            ? new Line(new Vector(splitAxis1, along1), new Vector(splitAxis2, along2))
            : new Line(new Vector(along1, splitAxis1), new Vector(along2, splitAxis2));
    }
}
