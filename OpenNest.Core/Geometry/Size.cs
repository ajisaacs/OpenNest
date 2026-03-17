using System;

namespace OpenNest.Geometry
{
    public struct Size
    {
        public Size(double width, double length)
        {
            Width = width;
            Length = length;
        }

        public double Width;

        public double Length;

        public static Size Parse(string size)
        {
            var a = size.ToUpper().Split('X');

            if (a.Length > 2)
                throw new FormatException("Invalid size format.");

            var width = double.Parse(a[0]);
            var length = double.Parse(a[1]);

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

        public override string ToString() => $"{Width} x {Length}";
        
        public string ToString(int decimalPlaces) => $"{System.Math.Round(Width, decimalPlaces)} x {System.Math.Round(Length, decimalPlaces)}";
    }
}
