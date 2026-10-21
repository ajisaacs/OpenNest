using System.Collections.Generic;
using OpenNest.Engine.Fill;
using OpenNest.Math;

namespace OpenNest.Engine.Strategies;

/// <summary>
/// Runs after the other strategies and adds tilted parts beside their best layout, where the
/// leftover strip is too narrow for the part at 0 or 90 degrees. See <see cref="TiltedRemnantFiller"/>.
/// </summary>
public class TiltedRemnantStrategy : IFillStrategy
{
    public string Name => "TiltedRemnant";
    public NestPhase Phase => NestPhase.Custom;
    public int Order => 500;

    public List<Part> Fill(FillContext context)
    {
        var layout = context.CurrentBest;
        if (layout == null || layout.Count == 0 || context.PartType == PartType.Circle)
            return null;

        // Tilting would break an explicit rotation range.
        var item = context.Item;
        if (!item.RotationStart.IsEqualTo(0) || !item.RotationEnd.IsEqualTo(0))
            return null;

        var target = context.MaxQuantity > 0 ? context.MaxQuantity : item.Quantity;
        if (target > 0 && layout.Count >= target)
            return null;

        var filler = new TiltedRemnantFiller(
            context.WorkArea,
            context.Plate.PartSpacing,
            context.Policy.Comparer
        );
        var result = filler.Fill(item.Drawing, layout, context.Token);

        if (result != null)
            context.ReportProgress(result, $"Tilted remnant: {result.Count} parts");

        return result;
    }
}
