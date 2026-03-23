namespace OpenNest.Posts.Cincinnati
{
    public sealed class CoordinateFormatter
    {
        private readonly int _accuracy;
        private readonly string _format;

        public CoordinateFormatter(int accuracy)
        {
            _accuracy = accuracy;
            _format = "0." + new string('#', accuracy);
        }

        public string FormatCoord(double value)
        {
            return System.Math.Round(value, _accuracy)
                .ToString(_format, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string Comment(string text) => $"( {text} )";

        public static string InlineComment(string text) => $"({text})";
    }
}
