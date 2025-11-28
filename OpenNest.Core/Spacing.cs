namespace OpenNest
{
    public struct Spacing
    {
        public Spacing(double topBottom, double leftRight)
        {
            Top = Bottom = topBottom;
            Left = Right = leftRight;
        }

        public Spacing(double left, double bottom, double right, double top)
        {
            Left = left;
            Bottom = bottom;
            Right = right;
            Top = top;
        }

        public double Left;

        public double Bottom;

        public double Right;

        public double Top;
    }
}
