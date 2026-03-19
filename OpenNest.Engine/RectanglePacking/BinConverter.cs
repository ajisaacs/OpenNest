using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;

namespace OpenNest.RectanglePacking
{
    internal static class BinConverter
    {
        public static Bin CreateBin(Box area, double partSpacing)
        {
            var bin = new Bin
            {
                Location = area.Location,
                Size = area.Size
            };

            bin.Width += partSpacing;
            bin.Length += partSpacing;

            return bin;
        }

        public static Item ToItem(NestItem item, double partSpacing, int id = 0)
        {
            var box = item.Drawing.Program.BoundingBox();

            box.Width += partSpacing;
            box.Length += partSpacing;

            return new Item
            {
                Id = id,
                Location = box.Location,
                Size = box.Size
            };
        }

        public static List<Item> ToItems(List<NestItem> items, double partSpacing, double plateArea)
        {
            var binItems = new List<Item>();

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var binItem = ToItem(item, partSpacing, i);

                var maxQty = (int)System.Math.Floor(plateArea / binItem.Area());
                var qty = item.Quantity < maxQty ? item.Quantity : maxQty;

                for (var j = 0; j < qty; j++)
                    binItems.Add(binItem.Clone() as Item);
            }

            return binItems;
        }

        public static List<Part> ToParts(Bin bin, List<NestItem> items)
        {
            var parts = new List<Part>();

            foreach (var item in bin.Items)
            {
                var nestItem = items[item.Id];
                var part = ToPart(item, nestItem.Drawing);
                parts.Add(part);
            }

            return parts;
        }

        private static Part ToPart(Item item, Drawing dwg)
        {
            var part = new Part(dwg);

            if (item.IsRotated)
                part.Rotate(Angle.HalfPI);

            var boundingBox = part.Program.BoundingBox();
            var offset = item.Location - boundingBox.Location;

            part.Offset(offset);

            return part;
        }
    }
}
