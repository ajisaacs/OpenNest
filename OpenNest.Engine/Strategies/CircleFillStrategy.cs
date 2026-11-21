using System.Collections.Generic;
using System.Linq;
using OpenNest.Engine.CirclePacking;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Cutouts;
using OpenNest.Geometry;

namespace OpenNest.Engine.Strategies;

/// <summary>
/// Staggers round parts edge to edge with <see cref="StaggeredFill"/>, whose half-pitch rows stretch
/// their pitch to the sheet so the next row drops deeper than a fixed lattice does, and whose
/// slack-shifted rows keep every row full; the better of the two is kept. A remnant ranking also
/// weighs each mode's stripes in both directions, since stripes spanning the side the remnant runs
/// along can leave a deeper remnant than the direction placing the most discs.
/// </summary>
public class CircleFillStrategy : IFillStrategy
{
    public string Name => "Circle";
    public NestPhase Phase => NestPhase.Custom;
    public int Order => 170;

    public List<Part> Fill(FillContext context)
    {
        if (context.PartType != PartType.Circle)
            return null;

        var item = Item.TryCreate(PartGeometrySnapshot.FromProgram(context.Item.Drawing.Program));
        if (item == null)
            return null;

        // Half the gap on every disc, and the work area grown to match, so discs keep the spacing
        // from each other and only their own radius from the work area's edges.
        var gap = context.Plate.PartSpacing + CutoutCirclePacker.Margin;
        var spaced = item.Clone();
        spaced.Radius += gap * 0.5;
        var area = context.WorkArea.Offset(gap * 0.5);

        var comparer = context.Policy?.Comparer ?? new DefaultFillComparer();
        var drawing = context.Item.Drawing;
        var halfPitch = Pack(area, spaced, StaggerMode.HalfPitch, item, drawing);
        var slackShift = Pack(area, spaced, StaggerMode.SlackShift, item, drawing);

        var best = comparer.IsBetter(slackShift, halfPitch, context.WorkArea) ? slackShift : halfPitch;
        if (comparer is not RemnantComparer)
            return best;

        foreach (var mode in new[] { StaggerMode.HalfPitch, StaggerMode.SlackShift })
            foreach (var alongX in new[] { true, false })
            {
                var candidate = Pack(area, spaced, mode, alongX, item, drawing);
                if (comparer.IsBetter(candidate, best, context.WorkArea))
                    best = candidate;
            }

        return best;
    }

    private static List<Part> Pack(
        Box area,
        Item spaced,
        StaggerMode mode,
        Item item,
        Drawing drawing
    )
    {
        var bin = NewBin(area);
        StaggeredFill.Fill(bin, spaced, mode);
        return ToParts(bin.Items, item, drawing);
    }

    private static List<Part> Pack(
        Box area,
        Item spaced,
        StaggerMode mode,
        bool alongX,
        Item item,
        Drawing drawing
    ) => ToParts(StaggeredFill.Layout(NewBin(area), spaced, mode, alongX), item, drawing);

    private static Bin NewBin(Box area) => new() { Location = area.Location, Size = area.Size };

    private static List<Part> ToParts(List<Item> items, Item item, Drawing drawing) =>
        items.Select(i => new Part(drawing, i.Center + item.PartOffset)).ToList();
}
