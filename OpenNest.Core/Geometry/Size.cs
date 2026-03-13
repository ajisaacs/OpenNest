using System;

namespace OpenNest.Geometry
{
    public struct Size
    {
        public Size(double width, double length)
        {
            Length = length;
            Width = width;
        }

        public double Length;

        public double Width;

        public static Size Parse(string size)
        {
            var a = size.ToUpper().Split('X');

            if (a.Length > 2)
                throw new FormatException("Invalid size format.");

            var length = double.Parse(a[0]);
            var width = double.Parse(a[1]);

            return new Size(width, length);
        }

        public static bool TryParse(string s, out Size size)
        {
            try
            {
                size = Parse(s);
            }
            catch
            {
                size = new Size(0, 0);
                return false;
            }

            return true;
        }

        public override string ToString()
        {
            return string.Format("{0} x {1}", Length, Width);
        }

        public string ToString(int decimalPlaces)
        {
            return string.Format("{0} x {1}", System.Math.Round(Length, decimalPlaces), System.Math.Round(Width, decimalPlaces));
        }
    }
}
