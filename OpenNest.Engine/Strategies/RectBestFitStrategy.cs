using System.Collections.Generic;
using OpenNest.Engine.RectanglePacking;

namespace OpenNest.Engine.Strategies
{
    public class RectBestFitStrategy : IFillStrategy
    {
        private const string ResultKey = "RectBestFit";

        public string Name => "RectBestFit";
        public NestPhase Phase => NestPhase.RectBestFit;
        public int Order => 200;

        public List<Part> Fill(FillContext context) => GetOrCompute(context);

        /// <summary>
        /// Packs the item's boxes once per context. It is cheap, so the pipeline also uses it as
        /// the provisional layout shown before the best-fit pairs are computed.
        /// </summary>
        internal static List<Part> GetOrCompute(FillContext context)
        {
            if (context.SharedState.TryGetValue(ResultKey, out var cached))
                return (List<Part>)cached;

            var binItem = BinConverter.ToItem(context.Item, context.Plate.PartSpacing);
            var bin = BinConverter.CreateBin(context.WorkArea, context.Plate.PartSpacing);

            RectFill.FillBest(bin, binItem);

            var parts = BinConverter.ToParts(bin, new List<NestItem> { context.Item });
            context.SharedState[ResultKey] = parts;
            return parts;
        }
    }
}
