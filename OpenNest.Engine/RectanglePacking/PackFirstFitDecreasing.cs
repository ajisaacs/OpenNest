using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.RectanglePacking
{
    internal class FirstFitDecreasing : PackEngine
    {
        private readonly List<Level> levels;

        public FirstFitDecreasing(Bin bin)
            : base(bin)
        {
            levels = new List<Level>();
        }

        public override void Pack(List<Item> items)
        {
            items = items.OrderBy(i => -i.Height).ToList();

            foreach (var item in items)
            {
                if (item.Height > Bin.Height)
                    continue;

                var level = FindLevel(item);

                if (level == null)
                    continue;

                level.AddItem(item);
            }
        }

        private Level FindLevel(Item item)
        {
            foreach (var level in levels)
            {
                if (level.Height < item.Height)
                    continue;

                if (level.RemainingWidth < item.Width)
                    continue;

                return level;
            }

            return CreateNewLevel(item);
        }

        private Level CreateNewLevel(Item item)
        {
            var y = Bin.Y;
            var lastLevel = levels.LastOrDefault();

            if (lastLevel != null)
                y = lastLevel.Y + lastLevel.Height;

            var remaining = Bin.Top - y;

            if (remaining < item.Height)
                return null;

            var level = new Level(Bin);
            level.Y = y;
            level.Height = item.Height;

            levels.Add(level);

            return level;
        }

        private class Level
        {
            public Level(Bin parent)
            {
                Parent = parent;
                NextItemLocation = parent.Location;
            }

            public Bin Parent { get; set; }

            private Vector NextItemLocation;

            public double X
            {
                get { return Parent.X; }
            }

            public double Y
            {
                get { return NextItemLocation.Y; }
                set { NextItemLocation.Y = value; }
            }

            public double Width
            {
                get { return Parent.Width; }
            }

            public double Height { get; set; }

            public double Top
            {
                get { return Y + Height; }
            }

            public double RemainingWidth
            {
                get { return X + Width - NextItemLocation.X; }
            }

            public void AddItem(Item item)
            {
                item.Location = NextItemLocation;
                Parent.Items.Add(item);

                NextItemLocation = new Vector(NextItemLocation.X + item.Width, NextItemLocation.Y);
            }
        }
    }
}
