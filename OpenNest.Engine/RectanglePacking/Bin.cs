using OpenNest.Geometry;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.RectanglePacking
{
    internal class Bin : Box
    {
        public Bin()
        {
            Items = new List<Item>();
        }

        public List<Item> Items { get; set; }

        public double Density()
        {
            return Items.Sum(i => i.Area()) / Area();
        }

        public object Clone()
        {
            return new Bin
            {
                Location = this.Location,
                Size = this.Size,
                Items = new List<Item>(Items)
            };
        }
    }
}
