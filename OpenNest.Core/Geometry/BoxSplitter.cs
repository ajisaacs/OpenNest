namespace OpenNest.Geometry
{
    public static class BoxSplitter
    {
        public static Box SplitTop(Box large, Box small)
        {
            if (!large.Intersects(small))
                return Box.Empty;

            var x = large.Left;
            var y = small.Top;
            var w = large.Width;
            var h = large.Top - y;

            return new Box(x, y, w, h);
        }

        public static Box SplitLeft(Box large, Box small)
        {
            if (!large.Intersects(small))
                return Box.Empty;

            var x = large.Left;
            var y = large.Bottom;
            var w = small.Left - x;
            var h = large.Length;

            return new Box(x, y, w, h);
        }

        public static Box SplitBottom(Box large, Box small)
        {
            if (!large.Intersects(small))
                return Box.Empty;

            var x = large.Left;
            var y = large.Bottom;
            var w = large.Width;
            var h = small.Top - y;

            return new Box(x, y, w, h);
        }

        public static Box SplitRight(Box large, Box small)
        {
            if (!large.Intersects(small))
                return Box.Empty;

            var x = small.Right;
            var y = large.Bottom;
            var w = large.Right - x;
            var h = large.Length;

            return new Box(x, y, w, h);
        }
    }
}
