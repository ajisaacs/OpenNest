using OpenNest.Engine.BestFit;
using OpenNest.Engine.Fill;
using System.Collections.Generic;

namespace OpenNest.Engine.Strategies
{
    public class PairsFillStrategy : IFillStrategy
    {
        public string Name => "Pairs";
        public NestPhase Phase => NestPhase.Pairs;
        public int Order => 100;

        public List<Part> Fill(FillContext context)
        {
            var filler = new PairFiller(context.Plate.Size, context.Plate.PartSpacing);
            var result = filler.Fill(context.Item, context.WorkArea,
                context.PlateNumber, context.Token, context.Progress);

            // Cache hit — PairFiller already called GetOrCompute internally.
            var bestFits = BestFitCache.GetOrCompute(
                context.Item.Drawing, context.Plate.Size.Length,
                context.Plate.Size.Width, context.Plate.PartSpacing);
            context.SharedState["BestFits"] = bestFits;

            return result;
        }
    }
}
