using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.RectanglePacking
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

        protected List<Item> FillGrid(Item item, int rows, int columns, int maxCount, bool columnMajor = true)
        {
            var items = new List<Item>();

            var outerCount = columnMajor ? columns : rows;
            var innerCount = columnMajor ? rows : columns;

            for (var i = 0; i < outerCount; i++)
            {
                for (var j = 0; j < innerCount; j++)
                {
                    var x = (columnMajor ? i : j) * item.Length + item.X;
                    var y = (columnMajor ? j : i) * item.Width + item.Y;

                    var clone = item.Clone() as Item;
                    clone.Location = new Vector(x, y);
                    items.Add(clone);

                    if (items.Count == maxCount)
                        return items;
                }
            }

            return items;
        }
    }
}
