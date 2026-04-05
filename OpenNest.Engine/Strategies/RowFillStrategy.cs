using System.Collections.Generic;
using OpenNest.Engine.Fill;

namespace OpenNest.Engine.Strategies;

public class RowFillStrategy : IFillStrategy
{
    public string Name => "Row";
    public NestPhase Phase => NestPhase.Custom;
    public int Order => 150;

    public List<Part> Fill(FillContext context)
    {
        if (context.PartType == PartType.Rectangle)
            return null;

        var filler = new StripeFiller(context, NestDirection.Horizontal) { CompleteStripesOnly = true };
        return filler.Fill();
    }
}
