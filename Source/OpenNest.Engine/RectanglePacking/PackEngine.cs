using System.Collections.Generic;

namespace OpenNest.RectanglePacking
{
    internal abstract class PackEngine
    {
        public PackEngine(Bin bin)
        {
            Bin = bin;
        }

        public Bin Bin { get; set; }

        public abstract void Pack(List<Item> items);
    }
}
