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
/// slack-shifted rows keep every row full; the better of the two is kept.
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
        var halfPitch = Pack(area, spaced, StaggerMode.HalfPitch, item, context.Item.Drawing);
        var slackShift = Pack(area, spaced, StaggerMode.SlackShift, item, context.Item.Drawing);

        return comparer.IsBetter(slackShift, halfPitch, context.WorkArea) ? slackShift : halfPitch;
    }

    private static List<Part> Pack(
        Box area,
        Item spaced,
        StaggerMode mode,
        Item item,
        Drawing drawing
    )
    {
        var bin = new Bin { Location = area.Location, Size = area.Size };
        StaggeredFill.Fill(bin, spaced, mode);
        return bin.Items.Select(i => new Part(drawing, i.Center + item.PartOffset)).ToList();
    }
}
