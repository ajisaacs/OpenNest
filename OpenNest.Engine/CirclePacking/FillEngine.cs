
namespace OpenNest.CirclePacking
{
    internal abstract class FillEngine
    {
        public FillEngine(Bin bin)
        {
            Bin = bin;
        }

        public Bin Bin { get; set; }

        public abstract void Fill(Item item);

        public abstract void Fill(Item item, int maxCount);
    }
}
