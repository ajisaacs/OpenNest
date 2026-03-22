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
            var comparer = context.Policy?.Comparer;
            var dedup = GridDedup.GetOrCreate(context.SharedState);
            var filler = new PairFiller(context.Plate, comparer, dedup);
            var result = filler.Fill(context.Item, context.WorkArea,
                context.PlateNumber, context.Token, context.Progress);

            context.SharedState["BestFits"] = result.BestFits;

            return result.Parts;
        }
    }
}
