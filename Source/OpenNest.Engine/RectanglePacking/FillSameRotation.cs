
namespace OpenNest.RectanglePacking
{
    internal class FillSameRotation : FillEngine
    {
        public FillSameRotation(Bin bin)
            : base(bin)
        {
        }

        public override void Fill(Item item)
        {
            var bin1 = Bin.Clone() as Bin;
            var bin2 = Bin.Clone() as Bin;

            var engine = new FillNoRotation(bin1);
            engine.Fill(item);

            item.Rotate();

            engine.Bin = bin2;
            engine.Fill(item);

            var density1 = bin1.Density();
            var density2 = bin2.Density();

            if (density1.IsEqualTo(density2))
            {
                var bounds1 = bin1.Items.GetBoundingBox();
                var bounds2 = bin2.Items.GetBoundingBox();

                if (bounds2.Right < bounds1.Right)
                    Bin.Items.AddRange(bin2.Items);
                else
                    Bin.Items.AddRange(bin1.Items);
            }
            else if (density1 > density2)
                Bin.Items.AddRange(bin1.Items);
            else
                Bin.Items.AddRange(bin2.Items);
        }

        public override void Fill(Item item, int maxCount)
        {
            var bin1 = Bin.Clone() as Bin;
            var bin2 = Bin.Clone() as Bin;

            var engine = new FillNoRotation(bin1);
            engine.Fill(item, maxCount);

            item.Rotate();

            engine.Bin = bin2;
            engine.Fill(item, maxCount);

            var bounds1 = bin1.Items.GetBoundingBox();
            var bounds2 = bin2.Items.GetBoundingBox();

            if (bounds2.Right < bounds1.Right)
                Bin.Items.AddRange(bin2.Items);
            else
                Bin.Items.AddRange(bin1.Items);
        }
    }
}
