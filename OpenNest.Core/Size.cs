using System;

namespace OpenNest
{
    public struct Size
    {
        public Size(double width, double height)
        {
            Height = height;
            Width = width;
        }

        public double Height;

        public double Width;

        public static Size Parse(string size)
        {
            var a = size.ToUpper().Split('X');

            if (a.Length > 2)
                throw new FormatException("Invalid size format.");

            var height = double.Parse(a[0]);
            var width = double.Parse(a[1]);

            return new Size(width, height);
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
            return string.Format("{0} x {1}", Height, Width);
        }

        public string ToString(int decimalPlaces)
        {
            return string.Format("{0} x {1}", System.Math.Round(Height, decimalPlaces), System.Math.Round(Width, decimalPlaces));
        }
    }
}
