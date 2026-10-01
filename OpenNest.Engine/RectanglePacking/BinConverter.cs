using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.RectanglePacking
{
    internal static class BinConverter
    {
        public static Bin CreateBin(Box area, double partSpacing)
        {
            var bin = new Bin { Location = area.Location, Size = area.Size };

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
                Size = box.Size,
            };
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
