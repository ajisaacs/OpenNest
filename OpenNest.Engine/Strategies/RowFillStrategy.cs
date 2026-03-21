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
        var filler = new StripeFiller(context, NestDirection.Horizontal);
        return filler.Fill();
    }
}
