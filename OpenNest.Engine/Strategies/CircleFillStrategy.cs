using System.Collections.Generic;
using System.Linq;
using OpenNest.Engine.CirclePacking;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Cutouts;
using OpenNest.Geometry;

namespace OpenNest.Engine.Strategies;

/// <summary>
/// Staggers round parts edge to edge with <see cref="FillEndOdd"/> and <see cref="FillEndEven"/>,
/// which stretch a row's pitch to the sheet so the next row drops deeper than a fixed lattice does.
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
        var odd = Pack(area, spaced, bin => new FillEndOdd(bin), item, context.Item.Drawing);
        var even = Pack(area, spaced, bin => new FillEndEven(bin), item, context.Item.Drawing);

        return comparer.IsBetter(even, odd, context.WorkArea) ? even : odd;
    }

    private static List<Part> Pack(
        Box area,
        Item spaced,
        System.Func<Bin, FillEngine> engine,
        Item item,
        Drawing drawing
    )
    {
        var bin = new Bin { Location = area.Location, Size = area.Size };
        engine(bin).Fill(spaced);
        return bin.Items.Select(i => new Part(drawing, i.Center + item.PartOffset)).ToList();
    }
}
