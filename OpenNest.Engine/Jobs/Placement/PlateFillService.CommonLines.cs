using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.CommonLines;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs.Placement;

/// <summary>A proposed fill: new parts and the common-line groups among them (empty when none).</summary>
public sealed record PlateFillProposal(List<Part> Parts, IReadOnlyList<CommonLineGroup> CommonLines);

public static partial class PlateFillService
{
    /// <summary>
    /// <see cref="FillItem(string, Plate, NestItem, Box, IProgress{NestProgress}, CancellationToken)"/>
    /// at a common-line usage level. With the level off, a drawing that does not allow common lines,
    /// or no positive kerf, it is exactly that fill. Otherwise the area is also filled with kerf-gap
    /// pairs of the drawing (<see cref="CommonLinePair"/>), each expanded into its two parts and a
    /// group, and the pair fill replaces the plain one when it places more parts, or (at
    /// <see cref="CommonLineUsage.Whenever"/>) the same number and the strategy's own ranking does
    /// not prefer the plain fill. Ties at <see cref="CommonLineUsage.WhenItAddsParts"/> keep the
    /// plain fill. The proposal is not committed: the caller adds the parts, then the groups.
    /// </summary>
    public static PlateFillProposal FillItem(
        string strategy,
        Plate plate,
        NestItem item,
        Box workArea,
        CommonLineUsage usage,
        double kerf,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(item);
        var plain = FillItem(strategy, plate, item, workArea, progress, token);
        var none = new PlateFillProposal(plain, Array.Empty<CommonLineGroup>());
        if (usage == CommonLineUsage.Off || item.Drawing?.AllowCommonLine != true
            || !double.IsFinite(kerf) || kerf <= 0 || !(kerf < plate.PartSpacing)
            || item.Quantity is > 0 and < 2 || token.IsCancellationRequested)
            return none;
        var constraints = item.Drawing.Constraints;
        // The second copy turns half a turn, so a fixed rotation must allow its 180° equivalent.
        if (item.StepAngle > 0 && constraints?.Allow180Equivalent != true)
            return none;

        CommonLinePair pair;
        try
        {
            pair = CommonLinePair.Build(PartGeometrySnapshot.FromProgram(item.Drawing.Program), kerf);
        }
        catch (NotSupportedException)
        {
            return none;
        }
        if (pair == null)
            return none;

        var proxy = new Drawing(item.Drawing.Name, DrawingJobMapper.ToProgram(pair.Geometry))
        {
            Priority = item.Drawing.Priority,
            Constraints = constraints,
        };
        var proxyItem = new NestItem
        {
            Drawing = proxy,
            Priority = item.Priority,
            Quantity = item.Quantity > 0 ? item.Quantity / 2 : item.Quantity,
            StepAngle = item.StepAngle,
            RotationStart = item.RotationStart,
            RotationEnd = item.RotationEnd,
        };
        // Previews show the plain fill; the pair fill is compared once both are done.
        var pairs = FillItem(strategy, plate, proxyItem, workArea, null, token);
        if (pairs.Count == 0 || token.IsCancellationRequested)
            return none;

        var parts = new List<Part>(2 * pairs.Count);
        var groups = new List<CommonLineGroup>(pairs.Count);
        foreach (var placed in pairs)
        {
            var rotation = placed.Rotation;
            var first = Place(item.Drawing, rotation, placed.Location);
            var sine = System.Math.Sin(rotation);
            var cosine = System.Math.Cos(rotation);
            var second = Place(item.Drawing, rotation + CommonLinePair.SecondRotation,
                new Vector(placed.Location.X + pair.SecondX * cosine - pair.SecondY * sine,
                    placed.Location.Y + pair.SecondX * sine + pair.SecondY * cosine));
            parts.Add(first);
            parts.Add(second);
            groups.Add(new CommonLineGroup(new[] { first, second }, kerf));
        }

        if (parts.Count < plain.Count)
            return none;
        if (parts.Count == plain.Count)
        {
            if (usage != CommonLineUsage.Whenever
                || RequireFiller(strategy, plate).IsBetterFill(plain, parts, workArea))
                return none;
        }
        return new PlateFillProposal(parts, groups);
    }

    // The proxy's frame is the drawing's own: rotate about the program origin, then move.
    private static Part Place(Drawing drawing, double rotation, Vector location)
    {
        var part = new Part(drawing);
        part.Rotate(rotation);
        part.Location = location;
        part.UpdateBounds();
        return part;
    }
}
