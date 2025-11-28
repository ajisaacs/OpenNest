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

        /// <summary>
        /// Vertical pattern.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="rows"></param>
        /// <param name="columns"></param>
        /// <param name="maxCount"></param>
        protected List<Item> VPattern(Item item, int rows, int columns, int maxCount)
        {
            var items = new List<Item>();

            for (int i = 0; i < columns; i++)
            {
                var x = item.Width * i + item.X;

                for (int j = 0; j < rows; j++)
                {
                    var y = item.Height * j + item.Y;

                    var addedItem = item.Clone() as Item;
                    addedItem.Location = new Vector(x, y);

                    items.Add(addedItem);

                    if (items.Count == maxCount)
                        return items;
                }
            }

            return items;
        }

        /// <summary>
        /// Horizontal pattern.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="rows"></param>
        /// <param name="columns"></param>
        /// <param name="maxCount"></param>
        protected List<Item> HPattern(Item item, int rows, int columns, int maxCount)
        {
            var items = new List<Item>();

            for (int i = 0; i < rows; i++)
            {
                var y = item.Height * i + item.Y;

                for (int j = 0; j < rows; j++)
                {
                    var x = item.Width * j + item.X;

                    var addedItem = item.Clone() as Item;
                    addedItem.Location = new Vector(x, y);

                    items.Add(addedItem);

                    if (items.Count == maxCount)
                        return items;
                }
            }

            return items;
        }
    }
}
