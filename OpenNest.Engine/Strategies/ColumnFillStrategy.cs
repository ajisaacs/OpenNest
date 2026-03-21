using System.Collections.Generic;
using OpenNest.Engine.Fill;

namespace OpenNest.Engine.Strategies;

public class ColumnFillStrategy : IFillStrategy
{
    public string Name => "Column";
    public NestPhase Phase => NestPhase.Custom;
    public int Order => 160;

    public List<Part> Fill(FillContext context)
    {
        var filler = new StripeFiller(context, NestDirection.Vertical) { CompleteStripesOnly = true };
        return filler.Fill();
    }
}
