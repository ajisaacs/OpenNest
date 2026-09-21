using System.Collections.Generic;

namespace OpenNest.Engine.RectanglePacking
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
