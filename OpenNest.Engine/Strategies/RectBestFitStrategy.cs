using System.Collections.Generic;
using OpenNest.RectanglePacking;

namespace OpenNest
{
    public class RectBestFitStrategy : IFillStrategy
    {
        public string Name => "RectBestFit";
        public NestPhase Phase => NestPhase.RectBestFit;
        public int Order => 200;

        public List<Part> Fill(FillContext context)
        {
            var binItem = BinConverter.ToItem(context.Item, context.Plate.PartSpacing);
            var bin = BinConverter.CreateBin(context.WorkArea, context.Plate.PartSpacing);

            var engine = new FillBestFit(bin);
            engine.Fill(binItem);

            return BinConverter.ToParts(bin, new List<NestItem> { context.Item });
        }
    }
}
