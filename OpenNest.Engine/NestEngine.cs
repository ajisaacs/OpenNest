using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;
using OpenNest.RectanglePacking;

namespace OpenNest
{
    public class NestEngine
    {
        public NestEngine(Plate plate)
        {
            Plate = plate;
        }

        public Plate Plate { get; set; }

        public NestDirection NestDirection { get; set; }

        public bool Fill(NestItem item)
        {
            var workArea = Plate.WorkArea();
            return FillArea(workArea, item);
        }

        public bool Fill(NestItem item, int maxCount)
        {
            var workArea = Plate.WorkArea();
            return FillArea(workArea, item, maxCount);
        }

        public bool FillArea(Box box, NestItem item)
        {
            var binItem = ConvertToRectangleItem(item);

            var bin = new Bin
            {
                Location = box.Location,
                Size = box.Size
            };

            bin.Width += Plate.PartSpacing;
            bin.Height += Plate.PartSpacing;

            var engine = new FillBestFit(bin);
            engine.Fill(binItem);

            var nestItems = new List<NestItem>();
            nestItems.Add(item);

            var parts = ConvertToParts(bin, nestItems);
            Plate.Parts.AddRange(parts);

            return parts.Count > 0;
        }

        public bool FillArea(Box box, NestItem item, int maxCount)
        {
            var binItem = ConvertToRectangleItem(item);

            var bin = new Bin
            {
                Location = box.Location,
                Size = box.Size
            };

            bin.Width += Plate.PartSpacing;
            bin.Height += Plate.PartSpacing;

            var engine = new FillBestFit(bin);
            engine.Fill(binItem, maxCount);

            var nestItems = new List<NestItem>();
            nestItems.Add(item);

            var parts = ConvertToParts(bin, nestItems);
            Plate.Parts.AddRange(parts);

            return parts.Count > 0;
        }

        public bool Pack(List<NestItem> items)
        {
            var workArea = Plate.WorkArea();
            return PackArea(workArea, items);
        }

        public bool PackArea(Box box, List<NestItem> items)
        {
            var binItems = ConvertToRectangleItems(items);

            var bin = new Bin
            {
                Location = box.Location,
                Size = box.Size
            };

            bin.Width += Plate.PartSpacing;
            bin.Height += Plate.PartSpacing;

            var engine = new PackBottomLeft(bin);
            engine.Pack(binItems);

            var parts = ConvertToParts(bin, items);
            Plate.Parts.AddRange(parts);

            return parts.Count > 0;
        }

        public bool FillLinear(NestItem item)
        {
            var workArea = Plate.WorkArea();
            var bestRotation = FindBestRotation(item);

            var engine = new FillLinear(workArea, Plate.PartSpacing);

            // Try 4 configurations: 2 rotations x 2 axes.
            var configs = new[]
            {
                engine.Fill(item.Drawing, bestRotation, NestDirection.Horizontal),
                engine.Fill(item.Drawing, bestRotation, NestDirection.Vertical),
                engine.Fill(item.Drawing, bestRotation + Angle.HalfPI, NestDirection.Horizontal),
                engine.Fill(item.Drawing, bestRotation + Angle.HalfPI, NestDirection.Vertical)
            };

            // Pick the configuration with the most parts.
            List<Part> best = null;

            foreach (var config in configs)
            {
                if (best == null || config.Count > best.Count)
                    best = config;
            }

            if (best == null || best.Count == 0)
                return false;

            // Limit to requested quantity if specified.
            if (item.Quantity > 0 && best.Count > item.Quantity)
                best = best.Take(item.Quantity).ToList();

            Plate.Parts.AddRange(best);
            return true;
        }

        private double FindBestRotation(NestItem item)
        {
            var entities = ConvertProgram.ToGeometry(item.Drawing.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid);

            var shapes = Helper.GetShapes(entities);

            if (shapes.Count == 0)
                return 0;

            // Find the largest shape (outer profile).
            Shape largest = shapes[0];
            double largestArea = largest.Area();

            for (int i = 1; i < shapes.Count; i++)
            {
                var area = shapes[i].Area();
                if (area > largestArea)
                {
                    largest = shapes[i];
                    largestArea = area;
                }
            }

            BoundingRectangleResult result;

            if (item.RotationStart.IsEqualTo(0) && item.RotationEnd.IsEqualTo(0))
                result = largest.FindBestRotation();
            else
                result = largest.FindBestRotation(item.RotationStart, item.RotationEnd);

            // Negate the angle to align the minimum bounding rectangle with the axes.
            return -result.Angle;
        }

        private List<Part> ConvertToParts(Bin bin, List<NestItem> items)
        {
            var parts = new List<Part>();

            foreach (var item in bin.Items)
            {
                var nestItem = items[item.Id];
                var part = ConvertToPart(item, nestItem.Drawing);
                parts.Add(part);
            }

            return parts;
        }

        private Part ConvertToPart(Item item, Drawing dwg)
        {
            var part = new Part(dwg);

            if (item.IsRotated)
                part.Rotate(Angle.HalfPI);

            var boundingBox = part.Program.BoundingBox();
            var offset = item.Location - boundingBox.Location;

            part.Offset(offset);

            return part;
        }

        private List<Item> ConvertToRectangleItems(List<NestItem> items)
        {
            var binItems = new List<Item>();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var binItem = ConvertToRectangleItem(item, i);

                int maxQty = (int)System.Math.Floor(Plate.Area() / binItem.Area());

                int qty = item.Quantity < maxQty
                    ? item.Quantity
                    : maxQty;

                for (int j = 0; j < qty; j++)
                    binItems.Add(binItem.Clone() as Item);
            }

            return binItems;
        }

        private Item ConvertToRectangleItem(NestItem item, int id = 0)
        {
            var box = item.Drawing.Program.BoundingBox();

            box.Width += Plate.PartSpacing;
            box.Height += Plate.PartSpacing;

            

            return new Item
            {
                Id = id,
                Location = box.Location,
                Size = box.Size
            };
        }
    }
}
