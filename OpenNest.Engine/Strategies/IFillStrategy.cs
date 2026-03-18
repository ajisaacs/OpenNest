using System.Collections.Generic;

namespace OpenNest
{
    public interface IFillStrategy
    {
        string Name { get; }
        NestPhase Phase { get; }
        int Order { get; }
        List<Part> Fill(FillContext context);
    }
}
