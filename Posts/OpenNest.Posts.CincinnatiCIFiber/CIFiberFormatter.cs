using System;
using System.Globalization;

namespace OpenNest.Posts.CincinnatiCIFiber
{
    /// <summary>
    /// Formats numeric values for the CI Fiber machine program.
    /// Coordinates: up to <see cref="PostedAccuracy"/> decimals with trailing
    /// zeros trimmed ("3.706", "5.01", "0"), never "-0" (sample NC format).
    /// Header values (V.E.*) use fixed decimals.
    /// </summary>
    public sealed class CIFiberFormatter
    {
        private readonly int _accuracy;
        private readonly string _trimFormat;
        private readonly string _fixedFormat;

        public CIFiberFormatter(int accuracy)
        {
            _accuracy = System.Math.Max(0, accuracy);
            _trimFormat = _accuracy == 0 ? "0" : "0." + new string('#', _accuracy);
            _fixedFormat = _accuracy == 0 ? "0" : "0." + new string('0', _accuracy);
        }

        /// <summary>
        /// Variable-trim coordinate/number format: rounded to the accuracy,
        /// trailing zeros removed, "0" for zero, never "-0".
        /// </summary>
        public string Coord(double value)
        {
            var rounded = System.Math.Round(value, _accuracy, MidpointRounding.AwayFromZero);
            if (rounded == 0.0)
                rounded = 0.0; // collapse -0

            return rounded.ToString(_trimFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>Fixed-decimal format for the V.E.* header variables.</summary>
        public string Fixed(double value)
        {
            var rounded = System.Math.Round(value, _accuracy, MidpointRounding.AwayFromZero);
            if (rounded == 0.0)
                rounded = 0.0;

            return rounded.ToString(_fixedFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>CRLF line writing helper matching the machine sample exactly.</summary>
        public static void Line(System.IO.TextWriter w, string text)
        {
            w.Write(text);
            w.Write("\r\n");
        }
    }
}
