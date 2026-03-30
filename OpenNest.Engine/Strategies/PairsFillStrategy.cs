using OpenNest.Engine.Fill;
using System.Collections.Generic;
using System.Threading;

namespace OpenNest.Engine.Strategies
{
    public class PairsFillStrategy : IFillStrategy
    {
        private static readonly AsyncLocal<bool> active = new();

        public string Name => "Pairs";
        public NestPhase Phase => NestPhase.Pairs;
        public int Order => 100;

        public List<Part> Fill(FillContext context)
        {
            // Prevent recursive PairFiller — remnant fills within PairFiller
            // create a new engine that runs the full pipeline, which would
            // invoke PairsFillStrategy again, causing deep recursion.
            if (active.Value)
                return null;

            if (context.PartType == PartType.Circle)
                return null;

            active.Value = true;
            try
            {
                var comparer = context.Policy?.Comparer;
                var dedup = GridDedup.GetOrCreate(context.SharedState);
                var filler = new PairFiller(context.Plate, comparer, dedup);
                var result = filler.Fill(context.Item, context.WorkArea,
                    context.PlateNumber, context.Token, context.Progress);

                context.SharedState["BestFits"] = result.BestFits;

                return result.Parts;
            }
            finally
            {
                active.Value = false;
            }
        }
    }
}
